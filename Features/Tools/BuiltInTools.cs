using System.Text.Json;
using KillerMCP.Models;
using KillerTools.Engine.Text;
using KillerTools.Engine.Math;
using KillerTools.Engine.Numbers;
using KillerTools.Engine.Reference;
using KillerTools.Engine.SystemTools;
using KillerTools.Engine.Documents;
using KillerTools.Engine.Photography;
using KillerTools.Engine.Network;
using KillerTools.Engine.Security;
using KillerTools.Engine.Web;

namespace KillerMCP.Features.Tools;

internal static partial class BuiltInTools
{
    private static readonly JsonSerializerOptions CamelCaseJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    // The companion's local web server starts the first time it is opened, not with every host, and a
    // failure to start is that tool's error rather than the host's.
    private static readonly object CompanionGate = new();
    private static BrowserCompanion? companion;

    private static object OpenCompanion()
    {
        try
        {
            lock (CompanionGate) return new { url = (companion ??= new BrowserCompanion()).Url };
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or InvalidOperationException or IOException)
        {
            throw new ToolCallException("The browser companion could not start: " + exception.Message);
        }
    }
    public static UpdateStatus Status { get; set; } = null!;

    public static object Call(string? name, JsonElement arguments) => name switch
    {
        "text_statistics" => TextStatistics.Calculate(arguments.GetProperty("text").GetString()!),
        "convert_case" => CaseConverter.Convert(arguments.GetProperty("text").GetString()!),
        "encode_base64" => new { encoded = Base64Text.Encode(arguments.GetProperty("text").GetString()!, OptionalBoolean(arguments, "urlSafe")) },
        "decode_base64" => new { text = Base64Text.Decode(arguments.GetProperty("encoded").GetString()!, OptionalBoolean(arguments, "urlSafe")) },
        "text_to_ascii_binary" => new { binary = AsciiBinary.Encode(arguments.GetProperty("text").GetString()!) },
        "ascii_binary_to_text" => new { text = AsciiBinary.Decode(arguments.GetProperty("binary").GetString()!) },
        "draw_ascii_text" => new { art = AsciiArt.Draw(arguments.GetProperty("text").GetString()!, OptionalString(arguments, "font", "Standard"), OptionalInteger(arguments, "width", 80)) },
        "search_emoji" => EmojiSearch.Search(Query(arguments), Limit(arguments)),
        "arabic_to_roman" => new { roman = RomanNumerals.FromArabic(arguments.GetProperty("number").GetInt32()) },
        "roman_to_arabic" => new { number = RomanNumerals.ToArabic(arguments.GetProperty("roman").GetString()!) },
        "text_to_nato_alphabet" => new { nato = NatoAlphabet.Convert(arguments.GetProperty("text").GetString()!) },
        "convert_integer_base" => new { value = IntegerBases.Convert(arguments.GetProperty("value").GetString()!, arguments.GetProperty("fromBase").GetInt32(), arguments.GetProperty("toBase").GetInt32()) },
        "convert_temperature" => Temperatures.Convert(arguments.GetProperty("value").GetDouble(), arguments.GetProperty("scale").GetString()!),
        "calculate_percentage" => Percentage(arguments),
        "escape_html_entities" => new { text = HtmlEntities.Escape(arguments.GetProperty("text").GetString()!) },
        "unescape_html_entities" => new { text = HtmlEntities.Unescape(arguments.GetProperty("text").GetString()!) },
        "markdown_to_html" => MarkdownToHtml(arguments),
        "format_sql" => FormatSql(arguments),
        "convert_date_time" => DateTimeConverter.Convert(arguments.GetProperty("value").GetString()!, arguments.GetProperty("inputFormat").GetString()!),
        "lookup_exchange_ndr" => Localize("exchange-ndr-lookup", MicrosoftReferenceLookups.SearchNdrs(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_group_policy" => Localize("group-policy-reference", GroupPolicyLookup.Search(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_http_status" => Localize("http-status-codes", HttpStatusLookup.Search(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_m365_sku" => Localize("m365-sku-decoder", MicrosoftReferenceLookups.SearchSkus(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_port_protocol" => Localize("port-protocol-reference", PortProtocolLookup.Search(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_windows_error" => Localize("windows-error-codes", MicrosoftReferenceLookups.SearchErrors(Query(arguments), Limit(arguments)), Locale(arguments)),
        "lookup_windows_event" => Localize("windows-event-lookup", MicrosoftReferenceLookups.SearchEvents(Query(arguments), Limit(arguments)), Locale(arguments)),
        "list_killer_modules" => KillerCatalogs.ListModules(OptionalNullableString(arguments, "name")),
        "list_killer_scripts" => KillerScripts(arguments),
        "search_powershell_cmdlets" => PowerShellBuilder.Search(Query(arguments), Limit(arguments)),
        "get_powershell_cmdlet" => PowerShellCmdlet(arguments),
        "build_powershell_command" => PowerShellCommand(arguments),
        "diff_text" => TextDiff.Compare(arguments.GetProperty("left").GetString()!, arguments.GetProperty("right").GetString()!),
        "format_json" => JsonFormat(arguments),
        "minify_json" => JsonMinify(arguments),
        "diff_json" => JsonDifference(arguments),
        "convert_xml_json" => XmlJson(arguments),
        "format_xml" => XmlFormat(arguments),
        "convert_json" => StructuredConvert(arguments, "json"),
        "convert_yaml" => StructuredConvert(arguments, "yaml"),
        "convert_toml" => StructuredConvert(arguments, "toml"),
        "format_yaml" => YamlFormat(arguments),
        "generate_meta_tags" => MetaTagResult(arguments),
        "search_gifs" => SearchGifs(arguments),
        "open_browser_companion_local" => OpenCompanion(),
        "get_browser_device_information_local" => BrowserState("device"),
        "get_browser_keycode_local" => BrowserState("key"),
        "get_browser_html_local" => BrowserState("html"),
        "get_browser_signature_local" => BrowserState("signature"),
        "get_browser_camera_local" => BrowserState("camera"),
        "describe_cron" => CronResult(arguments),
        "calculate_nd_exposure" => new { seconds = ExposureCalculators.WithNeutralDensity(arguments.GetProperty("baseSeconds").GetDouble(), arguments.GetProperty("stops").GetDouble()) },
        "calculate_exposure_equivalence" => new { shutterSeconds = ExposureCalculators.EquivalentShutter(arguments.GetProperty("shutterSeconds").GetDouble(), arguments.GetProperty("originalAperture").GetDouble(), arguments.GetProperty("targetAperture").GetDouble()) },
        "calculate_depth_of_field" => ExposureCalculators.DepthOfField(arguments.GetProperty("focalLengthMm").GetDouble(), arguments.GetProperty("aperture").GetDouble(), arguments.GetProperty("focusDistance").GetDouble(), arguments.GetProperty("focusUnit").GetString()!, arguments.GetProperty("circleOfConfusionMm").GetDouble()),
        "generate_svg_placeholder" => Svg(arguments),
        "generate_qr_code" => Qr(arguments),
        "calculate_chmod" => Chmod(arguments),
        "expand_ipv4_range" => Ipv4Tools.CoverRange(arguments.GetProperty("startIp").GetString()!, arguments.GetProperty("endIp").GetString()!),
        "calculate_ipv4_subnet" => Ipv4Subnet(arguments),
        "generate_ipv6_ula" => Ipv6Ula.Generate(arguments.GetProperty("macAddress").GetString()!, arguments.TryGetProperty("timestampMs", out var timestamp) ? timestamp.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        "json_to_csv" => new { csv = JsonCsv.Convert(arguments.GetProperty("rows")) },
        "parse_url" => UrlParser.Parse(arguments.GetProperty("url").GetString()!),
        "parse_user_agent" => UserAgentParser.Parse(arguments.GetProperty("userAgent").GetString()!),
        "parse_phone_number" => PhoneNumber(arguments),
        "convert_color" => Color(arguments),
        "lookup_mac_vendor" => MacVendor(arguments),
        "lookup_domain_dns" => DnsLookup(arguments),
        "lookup_domain_rdap" => RdapLookup(arguments),
        "lookup_cve" => Cve(arguments),
        "generate_spf_record" => new { record = EmailRecords.BuildSpf(StringArray(arguments, "providers"), StringArray(arguments, "ipAddresses"), OptionalString(arguments, "enforcement", "-all")) },
        "generate_dmarc_record" => new { record = EmailRecords.BuildDmarc(OptionalString(arguments, "policy", "reject"), OptionalString(arguments, "subdomainPolicy", ""), OptionalInteger(arguments, "percentage", 100), StringArray(arguments, "ruaEmails"), StringArray(arguments, "rufEmails"), OptionalString(arguments, "adkim", ""), OptionalString(arguments, "aspf", "")) },
        "parse_email_headers" => EmailHeaderParser.Parse(arguments.GetProperty("headers").GetString()!),
        "test_regex" => SafeRegex.Test(arguments.GetProperty("pattern").GetString()!, arguments.GetProperty("text").GetString()!, OptionalBoolean(arguments, "global", true), OptionalBoolean(arguments, "ignoreCase"), OptionalBoolean(arguments, "multiline"), OptionalBoolean(arguments, "dotAll", true)),
        "encode_file_base64_local" => EncodeFile(arguments),
        "decode_file_base64_local" => DecodeFile(arguments),
        "check_pdf_signatures_local" => CheckPdfSignatures(arguments),
        "otp_private" => Otp(arguments),
        "generate_otp_secret_private" => OtpTools.GenerateSecret(OptionalString(arguments, "issuer", "killer-tools"), OptionalString(arguments, "account", "demo-user")),
        "generate_ulids" => new { ids = IdentifierGenerators.GenerateUlids(OptionalInteger(arguments, "count", 1)) },
        "generate_uuids" => Uuids(arguments),
        "generate_lorem_ipsum" => new { text = LoremIpsum.Generate(OptionalInteger(arguments, "paragraphCount", 1), OptionalInteger(arguments, "sentencePerParagraph", 3), OptionalInteger(arguments, "wordCount", 10), OptionalBoolean(arguments, "startWithLoremIpsum", true), OptionalBoolean(arguments, "asHTML")) },
        "parse_jwt_private" => Jwt(arguments),
        "generate_rsa_keypair_private" => RsaKeyPairs.Generate(int.Parse(OptionalString(arguments, "bits", "2048"), System.Globalization.CultureInfo.InvariantCulture)),
        "crypt_text_private" => CryptText(arguments),
        "hash_text_private" => new { hash = TextHashes.Hash(arguments.GetProperty("value").GetString()!, OptionalString(arguments, "algorithm", "SHA256"), OptionalString(arguments, "encoding", "Hex")) },
        "hmac_private" => new { hmac = TextHashes.Hmac(arguments.GetProperty("value").GetString()!, arguments.GetProperty("secret").GetString()!, OptionalString(arguments, "algorithm", "SHA256"), OptionalString(arguments, "encoding", "Hex")) },
        "bcrypt_private" => Bcrypt(arguments),
        "bip39_private" => Bip39Result(arguments),
        "analyze_password_private" => PasswordStrength.Analyze(arguments.GetProperty("password").GetString()!),
        "generate_password_private" => GeneratePassword(arguments),
        "evaluate_math" => EvaluateMath(arguments),
        "list_film_stocks" => FilmReciprocity.List(),
        "calculate_reciprocity" => Reciprocity(arguments),
        "list_film_development_options" => DevelopmentOptions(),
        "calculate_film_development" => DevelopmentResult(arguments),
        "killermcp_update_status" => UpdateStatus(arguments),
        _ => throw new InvalidOperationException($"Unknown tool: {name}"),
    };

    private static bool OptionalBoolean(JsonElement arguments, string name, bool defaultValue = false) => arguments.TryGetProperty(name, out var value) ? value.GetBoolean() : defaultValue;
    private static string OptionalString(JsonElement arguments, string name, string defaultValue) => arguments.TryGetProperty(name, out var value) ? value.GetString()! : defaultValue;
    private static int OptionalInteger(JsonElement arguments, string name, int defaultValue) => arguments.TryGetProperty(name, out var value) ? value.GetInt32() : defaultValue;
    private static string[] StringArray(JsonElement arguments, string name) => arguments.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(value => value.GetString()!).ToArray() : [];
    private static string Query(JsonElement arguments) => arguments.GetProperty("query").GetString()!;
    private static int Limit(JsonElement arguments) => arguments.TryGetProperty("limit", out var value) ? value.GetInt32() : 10;
    private static string? OptionalNullableString(JsonElement arguments, string name) => arguments.TryGetProperty(name, out var value) ? value.GetString() : null;
}
