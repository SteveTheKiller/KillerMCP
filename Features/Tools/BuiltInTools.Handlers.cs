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
    private static object UpdateStatus(JsonElement arguments)
    {
        if (arguments.EnumerateObject().Any())
        {
            throw new ToolCallException("This tool takes no arguments");
        }

        return Status;
    }
    private static object BrowserState(string type)
    {
        try { return (companion ?? throw new InvalidOperationException("Open the browser companion and provide this input first")).Get(type); }
        catch (InvalidOperationException exception) { throw new ToolCallException(exception.Message); }
    }
    private static object PowerShellCmdlet(JsonElement arguments)
    {
        try { return PowerShellBuilder.Get(arguments.GetProperty("cmdlet").GetString()!); }
        catch (KeyNotFoundException exception) { throw new ToolCallException(exception.Message); }
    }
    private static object PowerShellCommand(JsonElement arguments)
    {
        try
        {
            var parameters = arguments.TryGetProperty("parameters", out var value) ? value : JsonSerializer.SerializeToElement(new { });
            return PowerShellBuilder.Build(arguments.GetProperty("cmdlet").GetString()!, parameters);
        }
        catch (KeyNotFoundException exception) { throw new ToolCallException(exception.Message); }
        catch (InvalidOperationException exception) { throw new ToolCallException(exception.Message); }
    }
    private static object GeneratePassword(JsonElement arguments)
    {
        try
        {
            var mode = OptionalString(arguments, "mode", "random");
            var password = mode switch
            {
                "passphrase" => PasswordGenerator.GeneratePassphrase(OptionalInteger(arguments, "wordCount", 6), OptionalString(arguments, "wordSeparator", "-"), OptionalBoolean(arguments, "capitalizeWords", true), OptionalBoolean(arguments, "appendNumber")),
                "pronounceable" => PasswordGenerator.GeneratePronounceable(OptionalInteger(arguments, "length", 20), OptionalBoolean(arguments, "appendNumber")),
                "format" => PasswordGenerator.GenerateFormatted(OptionalInteger(arguments, "length", 20), OptionalString(arguments, "format", "hex")),
                _ => PasswordGenerator.GenerateRandom(OptionalInteger(arguments, "length", 20), OptionalBoolean(arguments, "uppercase", true), OptionalBoolean(arguments, "lowercase", true), OptionalBoolean(arguments, "numbers", true), OptionalBoolean(arguments, "symbols", true), OptionalBoolean(arguments, "excludeAmbiguous", true), OptionalBoolean(arguments, "requireOneOfEach", true)),
            };
            return new { password };
        }
        catch (InvalidOperationException exception) { throw new ToolCallException(exception.Message); }
        catch (Exception) { throw new ToolCallException("Unable to generate password"); }
    }
    private static object Qr(JsonElement arguments)
    {
        try
        {
            string? value = null;
            if (arguments.GetProperty("mode").GetString() == "text") value = OptionalNullableString(arguments, "text");
            else if (arguments.TryGetProperty("wifi", out var wifi))
            {
                value = QrCodes.BuildWifiText(new WifiQrOptions(
                    wifi.GetProperty("ssid").GetString()!,
                    OptionalString(wifi, "password", ""),
                    OptionalString(wifi, "encryption", "WPA"),
                    OptionalNullableString(wifi, "eapMethod"),
                    OptionalBoolean(wifi, "isHiddenSSID"),
                    OptionalBoolean(wifi, "eapAnonymous"),
                    OptionalString(wifi, "eapIdentity", ""),
                    OptionalNullableString(wifi, "eapPhase2Method")));
            }
            if (string.IsNullOrEmpty(value)) throw new ToolCallException("Missing or invalid QR content");
            return new { svg = QrCodes.GenerateSvg(value, OptionalString(arguments, "foreground", "#000000"), OptionalString(arguments, "background", "#ffffff"), OptionalString(arguments, "errorCorrectionLevel", "medium")) };
        }
        catch (ToolCallException) { throw; }
        catch { throw new ToolCallException("Unable to generate QR code within output limit"); }
    }
    private static object Bip39Result(JsonElement arguments)
    {
        try
        {
            var entropy = OptionalNullableString(arguments, "entropy");
            var mnemonic = OptionalNullableString(arguments, "mnemonic");
            var language = OptionalString(arguments, "language", "English");
            if (entropy is not null && mnemonic is not null) throw new ToolCallException("Provide entropy or a mnemonic, not both");
            if (mnemonic is not null) return new { entropy = Bip39.MnemonicToEntropy(mnemonic, language), mnemonic };
            entropy ??= Bip39.GenerateEntropy();
            return new { entropy, mnemonic = Bip39.EntropyToMnemonic(entropy, language) };
        }
        catch (ToolCallException) { throw; }
        catch (Exception) { throw new ToolCallException("Invalid BIP39 input"); }
    }
    private static string Locale(JsonElement arguments) => OptionalString(arguments, "locale", "en");
    private static object Localize<T>(string tool, IReadOnlyList<T> entries, string locale)
    {
        if (locale.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return entries;
        }

        var source = JsonSerializer.SerializeToElement(entries, CamelCaseJson);
        return TranslateElement(tool, locale, source)!;
    }

    private static object? TranslateElement(string tool, string locale, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => TranslateElement(tool, locale, property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(item => TranslateElement(tool, locale, item)).ToArray(),
        JsonValueKind.String => ReferenceTranslations.Translate(tool, locale, element.GetString()!),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };
    private static object Percentage(JsonElement arguments)
    {
        var x = arguments.GetProperty("x").GetDouble();
        var y = arguments.GetProperty("y").GetDouble();
        var value = arguments.GetProperty("mode").GetString() switch
        {
            "percent_of" => Percentages.Of(x, y),
            "what_percent" => Percentages.Ratio(x, y),
            "change" => Percentages.Change(x, y),
            _ => throw new InvalidOperationException("Unknown percentage mode."),
        };
        return new { value };
    }
    private static object JsonFormat(JsonElement arguments)
    {
        try
        {
            var text = StructuredJson.Format(arguments.GetProperty("text").GetString()!, OptionalInteger(arguments, "indentSize", 3), OptionalBoolean(arguments, "sortKeys", true));
            if (text.Length > 16_384) throw new ToolCallException("Output is too large");
            return new { text };
        }
        catch (ToolCallException) { throw; }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ToolCallException("Invalid structured text");
        }
    }
    private static object JsonMinify(JsonElement arguments)
    {
        try
        {
            var text = StructuredJson.Minify(arguments.GetProperty("text").GetString()!);
            if (text.Length > 16_384) throw new ToolCallException("Output is too large");
            return new { text };
        }
        catch (ToolCallException) { throw; }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ToolCallException("Invalid structured text");
        }
    }
    private static object JsonDifference(JsonElement arguments)
    {
        try
        {
            var value = StructuredJson.Diff(arguments.GetProperty("left").GetString()!, arguments.GetProperty("right").GetString()!, OptionalBoolean(arguments, "onlyDifferences", true));
            if (JsonSerializer.Serialize(value, CamelCaseJson).Length > 16_384) throw new ToolCallException("Output is too large");
            return value;
        }
        catch (ToolCallException) { throw; }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ToolCallException("Invalid document input");
        }
    }
    private static object XmlJson(JsonElement arguments)
    {
        try
        {
            var text = arguments.GetProperty("text").GetString()!;
            return new { text = arguments.GetProperty("direction").GetString() == "xml_to_json" ? XmlJsonConverter.XmlToJson(text) : XmlJsonConverter.JsonToXml(text) };
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException or InvalidOperationException)
        {
            throw new ToolCallException("Invalid document input");
        }
    }
    private static object XmlFormat(JsonElement arguments)
    {
        try
        {
            return new { text = XmlFormatter.Format(arguments.GetProperty("text").GetString()!, OptionalInteger(arguments, "indentSize", 2), OptionalBoolean(arguments, "collapseContent", true)) };
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentOutOfRangeException)
        {
            throw new ToolCallException("Invalid document input");
        }
    }
    private static object StructuredConvert(JsonElement arguments, string from)
    {
        try
        {
            var text = arguments.GetProperty("text").GetString()!;
            var to = arguments.GetProperty("to").GetString();
            var converted = (from, to) switch
            {
                ("json", "yaml") => StructuredTextConverter.JsonToYaml(text),
                ("json", "toml") => StructuredTextConverter.JsonToToml(text),
                ("yaml", "json") => StructuredTextConverter.YamlToJson(text),
                ("yaml", "toml") => StructuredTextConverter.YamlToToml(text),
                ("toml", "json") => StructuredTextConverter.TomlToJson(text),
                ("toml", "yaml") => StructuredTextConverter.TomlToYaml(text),
                _ => throw new FormatException(),
            };
            if (converted.Length > 16_384) throw new ToolCallException("Output is too large");
            return new { text = converted };
        }
        catch (ToolCallException) { throw; }
        catch (Exception) { throw new ToolCallException("Invalid structured text"); }
    }
    private static object YamlFormat(JsonElement arguments)
    {
        try
        {
            var text = StructuredTextConverter.FormatYaml(arguments.GetProperty("text").GetString()!, OptionalInteger(arguments, "indentSize", 2), OptionalBoolean(arguments, "sortKeys"));
            if (text.Length > 16_384) throw new ToolCallException("Output is too large");
            return new { text };
        }
        catch (ToolCallException) { throw; }
        catch (Exception) { throw new ToolCallException("Invalid structured text"); }
    }
    private static object MetaTagResult(JsonElement arguments)
    {
        try
        {
            var type = arguments.TryGetProperty("type", out var typeElement) ? typeElement.GetString()! : "website";
            var fields = arguments.TryGetProperty("fields", out var fieldsElement)
                ? fieldsElement.EnumerateObject().Select(field => new KeyValuePair<string, string>(field.Name, field.Value.GetString()!))
                : [];
            var html = MetaTags.Generate(type, fields);
            if (html.Length > 32_768) throw new ToolCallException("Generated meta tags are too large");
            return new { html };
        }
        catch (ToolCallException) { throw; }
        catch (ArgumentException exception) when (exception.Message.StartsWith("Too many metadata fields", StringComparison.Ordinal)) { throw new ToolCallException("Too many metadata fields"); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("Unknown metadata field", StringComparison.Ordinal)) { throw new ToolCallException("Unknown metadata field for selected page type"); }
        catch (Exception) { throw new ToolCallException("Unable to generate meta tags"); }
    }
    private static object CronResult(JsonElement arguments)
    {
        try
        {
            return new
            {
                description = CronDescriptions.Describe(
                    arguments.GetProperty("expression").GetString()!,
                    OptionalBoolean(arguments, "use24HourTimeFormat", true),
                    OptionalBoolean(arguments, "dayOfWeekStartIndexZero", true))
            };
        }
        catch (Exception) { throw new ToolCallException("Invalid cron expression"); }
    }
    private static int? BcryptCost(string hash) =>
        hash.Length == 60 && hash[0] == '$' && hash[1] == '2' && hash[2] is 'a' or 'b' or 'x' or 'y' && hash[3] == '$' && hash[6] == '$'
            && char.IsAsciiDigit(hash[4]) && char.IsAsciiDigit(hash[5])
            ? (hash[4] - '0') * 10 + (hash[5] - '0')
            : null;

    private static object Bcrypt(JsonElement arguments)
    {
        try
        {
            var value = arguments.GetProperty("value").GetString()!;
            if (arguments.GetProperty("action").GetString() == "compare")
            {
                if (!arguments.TryGetProperty("hash", out var hash) || string.IsNullOrEmpty(hash.GetString())) throw new ToolCallException("A bcrypt hash is required");
                // The hash names its own cost, and each step doubles the work, so a compare is held to the
                // same 4 to 14 that hashing allows before bcrypt starts.
                if (BcryptCost(hash.GetString()!) is not (>= 4 and <= 14)) throw new ToolCallException("The bcrypt hash must be a $2a$, $2b$, $2x$ or $2y$ hash with a cost from 4 to 14");
                return new { matches = PasswordBcrypt.Verify(value, hash.GetString()!) };
            }
            return new { hash = PasswordBcrypt.Hash(value, OptionalInteger(arguments, "rounds", 10)) };
        }
        catch (ToolCallException) { throw; }
        catch (Exception) { throw new ToolCallException("Invalid bcrypt input"); }
    }
    private static object CryptText(JsonElement arguments)
    {
        try
        {
            string value = arguments.GetProperty("value").GetString()!;
            string secret = arguments.GetProperty("secret").GetString()!;
            string algorithm = OptionalString(arguments, "algorithm", "AES");
            string output = arguments.GetProperty("action").GetString() == "encrypt"
                ? CryptoJsText.Encrypt(value, secret, algorithm)
                : CryptoJsText.Decrypt(value, secret, algorithm);
            return new { output };
        }
        catch (Exception) { throw new ToolCallException("Unable to process encrypted text"); }
    }
    private static object DnsLookup(JsonElement arguments)
    {
        try
        {
            var result = DomainDnsLookup.LookupAsync(arguments.GetProperty("name").GetString()!, OptionalString(arguments, "type", "A")).GetAwaiter().GetResult();
            return new { status = result.Status, answers = result.Answers.Select(answer => new { name = answer.Name, type = answer.Type, ttl = answer.Ttl, data = answer.Data }) };
        }
        catch (Exception) { throw new ToolCallException("DNS lookup is unavailable"); }
    }
    private static object MarkdownToHtml(JsonElement arguments)
    {
        try { return new { text = MarkdownHtml.Convert(arguments.GetProperty("markdown").GetString()!) }; }
        catch (InvalidDataException) { throw new ToolCallException("Output is too large"); }
        catch (Exception) { throw new ToolCallException("Invalid Markdown"); }
    }
    private static object FormatSql(JsonElement arguments)
    {
        try
        {
            return new
            {
                text = SqlFormatter.Format(
                    arguments.GetProperty("sql").GetString()!,
                    OptionalString(arguments, "language", "sql"),
                    OptionalString(arguments, "keywordCase", "upper"),
                    OptionalString(arguments, "indentStyle", "standard"),
                    OptionalBoolean(arguments, "useTabs"))
            };
        }
        catch (InvalidDataException) { throw new ToolCallException("Output is too large"); }
        catch (Exception) { throw new ToolCallException("Invalid SQL formatting request"); }
    }
    private static object SearchGifs(JsonElement arguments)
    {
        try { return GifSearch.SearchAsync(arguments.GetProperty("query").GetString()!, Limit(arguments)).GetAwaiter().GetResult(); }
        catch (Exception) { throw new ToolCallException("GIF search is unavailable"); }
    }
    private static object RdapLookup(JsonElement arguments)
    {
        try
        {
            var result = DomainRdapLookup.LookupAsync(arguments.GetProperty("domain").GetString()!).GetAwaiter().GetResult();
            return new { domain = result.Domain, handle = result.Handle, status = result.Status, events = result.Events.Select(value => new { eventAction = value.EventAction, eventDate = value.EventDate }), nameservers = result.Nameservers, dnssecSigned = result.DnssecSigned };
        }
        catch (Exception) { throw new ToolCallException("RDAP lookup is unavailable for this domain"); }
    }
    private static object Cve(JsonElement arguments)
    {
        try { return CveLookup.LookupAsync(arguments.GetProperty("query").GetString()!).GetAwaiter().GetResult(); }
        catch { throw new ToolCallException("CVE lookup is unavailable"); }
    }
    private static object PhoneNumber(JsonElement arguments)
    {
        try
        {
            return PhoneNumberParser.Parse(arguments.GetProperty("phone").GetString()!, OptionalNullableString(arguments, "defaultCountry"));
        }
        catch (ArgumentException exception) when (exception.Message.StartsWith("Unknown default country code", StringComparison.Ordinal))
        {
            throw new ToolCallException("Unknown default country code");
        }
        catch
        {
            throw new ToolCallException("Invalid phone number");
        }
    }
    private static object Color(JsonElement arguments)
    {
        try { return ColorConverter.Convert(arguments.GetProperty("color").GetString()!); }
        catch { throw new ToolCallException("Invalid color"); }
    }
    private static object MacVendor(JsonElement arguments)
    {
        try { return MacVendorLookup.Lookup(arguments.GetProperty("macAddress").GetString()!); }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ToolCallException("Invalid MAC address");
        }
    }
    private static object KillerScripts(JsonElement arguments)
    {
        try
        {
            return KillerCatalogs.ListScriptsAsync(OptionalString(arguments, "query", "")).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        {
            throw new ToolCallException("Script catalog is unavailable");
        }
    }
    private static object Svg(JsonElement arguments)
    {
        var svg = SvgPlaceholder.Build(arguments.GetProperty("width").GetInt32(), arguments.GetProperty("height").GetInt32(),
            arguments.TryGetProperty("fontSize", out var fontSize) ? fontSize.GetInt32() : 26,
            arguments.TryGetProperty("bgColor", out var background) ? background.GetString()! : "#cccccc",
            arguments.TryGetProperty("fgColor", out var foreground) ? foreground.GetString()! : "#333333",
            !arguments.TryGetProperty("useExactSize", out var exact) || exact.GetBoolean(),
            arguments.TryGetProperty("customText", out var text) ? text.GetString()! : "");
        return new { svg, dataUrl = $"data:image/svg+xml;base64,{Base64Text.Encode(svg)}" };
    }
    private static object Chmod(JsonElement arguments)
    {
        var permissions = arguments.GetProperty("permissions");
        PermissionGroup Group(string name)
        {
            var group = permissions.GetProperty(name);
            return new(group.GetProperty("read").GetBoolean(), group.GetProperty("write").GetBoolean(), group.GetProperty("execute").GetBoolean());
        }
        return UnixPermissions.Calculate(new(Group("owner"), Group("group"), Group("public")));
    }
    private static object Ipv4Subnet(JsonElement arguments)
    {
        var subnet = Ipv4Tools.CalculateSubnet(arguments.GetProperty("address").GetString()!);
        var value = new Dictionary<string, object?>
        {
            ["cidr"] = subnet.Cidr,
            ["networkAddress"] = subnet.NetworkAddress,
            ["networkMask"] = subnet.NetworkMask,
            ["prefixLength"] = subnet.PrefixLength,
            ["wildcardMask"] = subnet.WildcardMask,
            ["size"] = subnet.Size,
            ["usableHosts"] = subnet.UsableHosts,
            ["firstAddress"] = subnet.FirstAddress,
            ["lastAddress"] = subnet.LastAddress,
            ["ipClass"] = subnet.IpClass,
        };
        if (subnet.BroadcastAddress is not null) value["broadcastAddress"] = subnet.BroadcastAddress;
        return value;
    }
    private static object EncodeFile(JsonElement arguments)
    {
        try
        {
            return Base64Files.Encode(arguments.GetProperty("path").GetString()!);
        }
        catch (InvalidDataException exception)
        {
            throw new ToolCallException(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ToolCallException("Unable to read file");
        }
    }
    private static object DecodeFile(JsonElement arguments)
    {
        try
        {
            return Base64Files.Decode(arguments.GetProperty("path").GetString()!, arguments.GetProperty("base64").GetString()!);
        }
        catch (FormatException exception)
        {
            throw new ToolCallException(exception.Message);
        }
        catch (InvalidDataException exception)
        {
            throw new ToolCallException(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ToolCallException("Unable to write file. Choose a new path.");
        }
    }
    private static object CheckPdfSignatures(JsonElement arguments)
    {
        try
        {
            var result = PdfSignatures.Inspect(arguments.GetProperty("path").GetString()!);
            if (JsonSerializer.Serialize(result, CamelCaseJson).Length > 65_536) throw new ToolCallException("Signature details exceed output limit");
            return result;
        }
        catch (ToolCallException) { throw; }
        catch (InvalidDataException exception) when (exception.Message == "PDF exceeds 8 MB limit") { throw new ToolCallException(exception.Message); }
        catch (Exception) { throw new ToolCallException("Unable to inspect PDF signatures"); }
    }
    private static object Otp(JsonElement arguments)
    {
        try
        {
            var mode = OptionalString(arguments, "mode", "totp");
            var timeStep = OptionalInteger(arguments, "timeStep", 30);
            var counter = mode == "totp"
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() / timeStep
                : arguments.TryGetProperty("counter", out var value) ? value.GetInt64() : throw new ToolCallException("A counter is required for HOTP");
            var secret = arguments.GetProperty("secret").GetString()!;
            if (arguments.TryGetProperty("code", out var code))
            {
                return new { matches = OtpTools.VerifyCode(secret, counter, code.GetString()!) };
            }
            return new { code = OtpTools.GenerateCode(secret, counter), counter };
        }
        catch (ToolCallException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ToolCallException("Invalid OTP secret");
        }
    }
    private static object Uuids(JsonElement arguments)
    {
        try
        {
            var ids = IdentifierGenerators.GenerateUuids(OptionalString(arguments, "version", "v4"), OptionalInteger(arguments, "count", 1), OptionalNullableString(arguments, "name"), OptionalNullableString(arguments, "namespace"));
            return new { ids };
        }
        catch (FormatException exception)
        {
            throw new ToolCallException(exception.Message);
        }
    }
    private static object Jwt(JsonElement arguments)
    {
        try
        {
            return JwtParser.Decode(arguments.GetProperty("token").GetString()!);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ToolCallException("Invalid JWT");
        }
    }
    private static object EvaluateMath(JsonElement arguments)
    {
        try
        {
            return new { value = BoundedMath.Evaluate(arguments.GetProperty("expression").GetString()!) };
        }
        catch (Exception exception) when (exception is FormatException or ArithmeticException)
        {
            throw new ToolCallException("Invalid or unsupported math expression");
        }
    }
    private static object Reciprocity(JsonElement arguments)
    {
        try
        {
            return FilmReciprocity.Calculate(arguments.GetProperty("filmStockId").GetString()!, arguments.GetProperty("meteredSeconds").GetDouble());
        }
        catch (KeyNotFoundException exception)
        {
            throw new ToolCallException(exception.Message);
        }
    }
    private static object DevelopmentOptions() => new
    {
        developers = FilmDevelopment.Developers,
        films = FilmDevelopment.Films.Select(film =>
        {
            var item = new Dictionary<string, object> { ["name"] = film.Name, ["isoBase"] = film.IsoBase };
            if (film.Process is not null) item["process"] = film.Process;
            return item;
        }),
    };
    private static object DevelopmentResult(JsonElement arguments)
    {
        try
        {
            return FilmDevelopment.Calculate(
                arguments.GetProperty("filmName").GetString()!,
                arguments.GetProperty("developerId").GetString()!,
                arguments.TryGetProperty("dilutionIndex", out var dilutionIndex) ? dilutionIndex.GetInt32() : null,
                arguments.TryGetProperty("baseSeconds", out var baseSeconds) ? baseSeconds.GetDouble() : null,
                arguments.TryGetProperty("temperatureC", out var temperature) ? temperature.GetDouble() : null,
                arguments.TryGetProperty("pushPullStops", out var stops) ? stops.GetDouble() : 0,
                arguments.TryGetProperty("tankMl", out var tank) ? tank.GetDouble() : 500);
        }
        catch (KeyNotFoundException exception) { throw new ToolCallException(exception.Message); }
        catch (ArgumentOutOfRangeException) { throw new ToolCallException("Invalid dilution index"); }
        catch (InvalidOperationException exception) { throw new ToolCallException(exception.Message); }
    }
}
