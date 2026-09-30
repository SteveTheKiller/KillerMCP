using System.Text;
using System.Text.Json;
using System.Reflection;
using KillerMCP;
using KillerMCP.Runtime;
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

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version!;
var currentVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
ToolRegistry.Status = await UpdateChecker.CheckForUpdateAsync(currentVersion);
UpdateChecker.StartMonitoring(currentVersion, ToolRegistry.Status, status => ToolRegistry.Status = status);
var appAdapters = new List<AppAdapter>();
var killerNotesPath = AppDiscovery.Discover("killernotes");
var killendarPath = AppDiscovery.Discover("killendar");
var killerShellPath = AppDiscovery.Discover("killershell");
var killerScanPath = AppDiscovery.Discover("killerscan");
var killerPdfPath = AppDiscovery.Discover("killerpdf");
appAdapters.AddRange(await KillerNotesAdapter.CreateAsync(killerNotesPath));
var killendar = await KillendarAdapter.CreateAsync(killendarPath);
if (killendar is not null)
{
    appAdapters.Add(killendar);
}
var killendarCreate = await KillendarAdapter.CreateAppointmentAsync(killendarPath);
if (killendarCreate is not null) appAdapters.Add(killendarCreate);
appAdapters.AddRange(await KillerShellAdapter.CreateAsync(killerShellPath));
appAdapters.AddRange(await KillerScanAdapter.CreateAsync(killerScanPath));
appAdapters.AddRange(await KillerPdfAdapter.CreateAsync(killerPdfPath));
appAdapters.AddRange(IntegrationAdapter.Create(killerNotesPath, killendarPath, killerShellPath, killerScanPath, killerPdfPath));
ToolRegistry.SetAdapters(appAdapters);
string? line;
while ((line = Console.ReadLine()) is not null)
{
    JsonElement request;
    try { request = JsonSerializer.Deserialize<JsonElement>(line); }
    catch { continue; }
    if (!request.TryGetProperty("id", out var id) || !request.TryGetProperty("method", out var methodElement))
    {
        continue;
    }
    var method = methodElement.GetString();
    object result = method switch
    {
        "initialize" => new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "KillerMCP", version = currentVersion },
            instructions = "Use KillerMCP tools by task. Exact tool names are optional when the request is clear." + UpdateChecker.Instruction(ToolRegistry.Status),
        },
        "tools/list" => new { tools = ToolRegistry.Tools },
        "tools/call" => ToolRegistry.Call(request.GetProperty("params"), json),
        _ => throw new InvalidOperationException($"Unknown MCP method: {method}"),
    };
    Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, json));
}

static class ToolRegistry
{
    private static readonly JsonSerializerOptions CamelCaseJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly BrowserCompanion Companion = new();
    private static IReadOnlyDictionary<string, AppAdapter> Adapters = new Dictionary<string, AppAdapter>();
    public static UpdateStatus Status { get; set; } = null!;

    private static readonly object[] CoreTools =
    [
        Tool("text_statistics", "Count characters, words, lines, and UTF-8 bytes.", new { text = StringSchema() }, ["text"]),
        Tool("convert_case", "Return the case conversions shown by KillerTools.", new { text = StringSchema() }, ["text"]),
        Tool("encode_base64", "Encode text as Base64.", new { text = StringSchema(), urlSafe = BooleanSchema(false) }, ["text"]),
        Tool("decode_base64", "Decode Base64 as text.", new { encoded = StringSchema(), urlSafe = BooleanSchema(false) }, ["encoded"]),
        Tool("text_to_ascii_binary", "Convert text to ASCII binary.", new { text = StringSchema() }, ["text"]),
        Tool("ascii_binary_to_text", "Convert ASCII binary to text.", new { binary = StringSchema() }, ["binary"]),
        Tool("draw_ascii_text", "Render text with a bundled KillerTools FIGlet font.", new { text = new { type = "string", minLength = 1, maxLength = 128 }, font = new { type = "string", minLength = 1, maxLength = 40, pattern = "^[A-Za-z0-9_ '-]+$", @default = "Standard" }, width = new { type = "integer", minimum = 20, maximum = 200, @default = 80 } }, ["text"]),
        Tool("search_emoji", "Find emoji characters by name, group, keyword, or Unicode code point using the KillerTools emoji catalog.", new { query = new { type = "string", minLength = 1, maxLength = 80 }, limit = new { type = "integer", minimum = 1, maximum = 30, @default = 10 } }, ["query"]),
        Tool("arabic_to_roman", "Convert an integer to Roman numerals.", new { number = IntegerSchema(1, 3999) }, ["number"]),
        Tool("roman_to_arabic", "Convert Roman numerals to an integer.", new { roman = StringSchema() }, ["roman"]),
        Tool("text_to_nato_alphabet", "Convert text to the NATO phonetic alphabet.", new { text = StringSchema() }, ["text"]),
        Tool("convert_integer_base", "Convert an integer between bases.", new { value = StringSchema(), fromBase = IntegerSchema(2, 64), toBase = IntegerSchema(2, 64) }, ["value", "fromBase", "toBase"]),
        Tool("convert_temperature", "Convert a temperature among supported scales.", new { value = NumberSchema(), scale = EnumSchema("kelvin", "celsius", "fahrenheit", "rankine", "delisle", "newton", "reaumur", "romer") }, ["value", "scale"]),
        Tool("calculate_percentage", "Calculate percentages and percentage changes.", new { mode = EnumSchema("percent_of", "what_percent", "change"), x = NumberSchema(), y = NumberSchema() }, ["mode", "x", "y"]),
        Tool("escape_html_entities", "Escape HTML entities.", new { text = StringSchema() }, ["text"]),
        Tool("unescape_html_entities", "Unescape HTML entities.", new { text = StringSchema() }, ["text"]),
        Tool("markdown_to_html", "Convert Markdown to HTML using KillerTools Markdown to HTML.", new { markdown = StringSchema() }, ["markdown"]),
        Tool("format_sql", "Format SQL with the dialect and style options shown by KillerTools SQL Prettify.", new { sql = StringSchema(), language = new { type = "string", @enum = new[] { "bigquery", "db2", "hive", "mariadb", "mysql", "n1ql", "plsql", "postgresql", "redshift", "spark", "sql", "sqlite", "tsql" }, @default = "sql" }, keywordCase = EnumSchema("upper", "lower", "preserve"), indentStyle = EnumSchema("standard", "tabularLeft", "tabularRight"), useTabs = BooleanSchema(false) }, ["sql"]),
        Tool("convert_date_time", "Convert a date among KillerTools formats.", new { value = StringSchema(), inputFormat = EnumSchema("iso8601", "iso9075", "rfc3339", "rfc7231", "unix_seconds", "timestamp_ms", "utc", "mongo_object_id", "excel") }, ["value", "inputFormat"]),
        LookupTool("lookup_exchange_ndr", "Search the Exchange non-delivery report reference."),
        LookupTool("lookup_group_policy", "Search the Group Policy reference."),
        LookupTool("lookup_http_status", "Search the HTTP status code reference."),
        LookupTool("lookup_m365_sku", "Search the Microsoft 365 SKU reference."),
        LookupTool("lookup_port_protocol", "Search the port and protocol reference."),
        LookupTool("lookup_windows_error", "Search the Windows error code reference."),
        LookupTool("lookup_windows_event", "Search the Windows Event ID reference."),
        Tool("list_killer_modules", "List the KillerTools PowerShell modules, commands, install text, and repository links.", new { name = new { type = "string", maxLength = 80 } }, []),
        Tool("list_killer_scripts", "List KillerScripts names, descriptions, and download links from GitHub.", new { query = new { type = "string", maxLength = 80, @default = "" } }, []),
        Tool("search_powershell_cmdlets", "Search the KillerTools PowerShell Builder catalog. Does not run commands.", new { query = new { type = "string", minLength = 1, maxLength = 80 }, limit = new { type = "integer", minimum = 1, maximum = 20, @default = 10 } }, ["query"]),
        Tool("get_powershell_cmdlet", "Get parameter descriptions and example snippets for an exact KillerTools PowerShell cmdlet. Does not run commands.", new { cmdlet = new { type = "string", minLength = 1, maxLength = 80 } }, ["cmdlet"]),
        Tool("build_powershell_command", "Assemble a PowerShell command from the KillerTools catalog. Returns text only and never executes it. Review scriptblock parameters before use.", new { cmdlet = new { type = "string", minLength = 1, maxLength = 80 }, parameters = new { type = "object", maxProperties = 20, additionalProperties = new { oneOf = new object[] { new { type = "string", maxLength = 512 }, new { type = "boolean" } } }, @default = new { } } }, ["cmdlet"]),
        Tool("diff_text", "Compare two texts line by line.", new { left = StringSchema(), right = StringSchema() }, ["left", "right"]),
        Tool("format_json", "Prettify JSON5-compatible text with optional key sorting.", new { text = StructuredTextSchema(), indentSize = new { type = "integer", minimum = 0, maximum = 10, @default = 3 }, sortKeys = BooleanSchema(true) }, ["text"]),
        Tool("minify_json", "Minify JSON5-compatible text.", new { text = StructuredTextSchema() }, ["text"]),
        Tool("diff_json", "Compare two JSON5-compatible documents.", new { left = StructuredTextSchema(), right = StructuredTextSchema(), onlyDifferences = BooleanSchema(true) }, ["left", "right"]),
        Tool("convert_xml_json", "Convert XML to JSON or JSON5-compatible text to XML using KillerTools.", new { text = StructuredTextSchema(), direction = EnumSchema("xml_to_json", "json_to_xml") }, ["text", "direction"]),
        Tool("format_xml", "Format XML using KillerTools XML Formatter.", new { text = StructuredTextSchema(), indentSize = new { type = "integer", minimum = 0, maximum = 10, @default = 2 }, collapseContent = BooleanSchema(true) }, ["text"]),
        Tool("convert_json", "Convert JSON5-compatible text to YAML or TOML using KillerTools.", new { text = StructuredTextSchema(), to = EnumSchema("yaml", "toml") }, ["text", "to"]),
        Tool("convert_yaml", "Convert YAML text to JSON or TOML using KillerTools.", new { text = StructuredTextSchema(), to = EnumSchema("json", "toml") }, ["text", "to"]),
        Tool("convert_toml", "Convert TOML text to JSON or YAML using KillerTools.", new { text = StructuredTextSchema(), to = EnumSchema("json", "yaml") }, ["text", "to"]),
        Tool("format_yaml", "Prettify YAML with optional key sorting using KillerTools YAML Viewer.", new { text = StructuredTextSchema(), indentSize = new { type = "integer", minimum = 1, maximum = 10, @default = 2 }, sortKeys = BooleanSchema(false) }, ["text"]),
        Tool("generate_meta_tags", "Generate Open Graph and Twitter meta tags using KillerTools.", new { type = EnumSchema("website", "article", "book", "profile", "music.song", "music.album", "music.playlist", "music.radio_station", "video.movie", "video.episode", "video.tv_show", "video.other"), fields = new { type = "object", maxProperties = 30, additionalProperties = new { type = "string", maxLength = 1024 } } }, []),
        Tool("search_gifs", "Search GIFs through the fixed KillerTools GIF proxy and return links and titles.", new { query = new { type = "string", minLength = 1, maxLength = 80 }, limit = new { type = "integer", minimum = 1, maximum = 20, @default = 10 } }, ["query"]),
        Tool("open_browser_companion_local", "Get the private local page for browser, camera, editor, and signature tools.", new { }, []),
        Tool("get_browser_device_information_local", "Read live device and browser information from the local companion page.", new { }, []),
        Tool("get_browser_keycode_local", "Read the most recent keyboard event from the local companion page.", new { }, []),
        Tool("get_browser_html_local", "Read the current HTML editor content from the local companion page.", new { }, []),
        Tool("get_browser_signature_local", "Read the drawn signature PNG from the local companion page.", new { }, []),
        Tool("get_browser_camera_local", "Read the photo or video captured in the local companion page.", new { }, []),
        Tool("describe_cron", "Validate and describe a cron expression using KillerTools.", new { expression = new { type = "string", minLength = 1, maxLength = 128 }, use24HourTimeFormat = BooleanSchema(true), dayOfWeekStartIndexZero = BooleanSchema(true) }, ["expression"]),
        Tool("calculate_nd_exposure", "Calculate exposure time with a neutral density filter.", new { baseSeconds = NumberSchema(), stops = NumberSchema() }, ["baseSeconds", "stops"]),
        Tool("calculate_exposure_equivalence", "Calculate equivalent shutter speed after an aperture change.", new { shutterSeconds = NumberSchema(), originalAperture = NumberSchema(), targetAperture = NumberSchema() }, ["shutterSeconds", "originalAperture", "targetAperture"]),
        Tool("calculate_depth_of_field", "Calculate depth of field.", new { focalLengthMm = NumberSchema(), aperture = NumberSchema(), focusDistance = NumberSchema(), focusUnit = EnumSchema("m", "ft"), circleOfConfusionMm = NumberSchema() }, ["focalLengthMm", "aperture", "focusDistance", "focusUnit", "circleOfConfusionMm"]),
        Tool("generate_svg_placeholder", "Generate an SVG placeholder image.", new { width = IntegerSchema(1, 4096), height = IntegerSchema(1, 4096), fontSize = new { type = "integer", minimum = 1, maximum = 512, @default = 26 }, bgColor = new { type = "string", pattern = "^#[0-9a-f]{6}$", @default = "#cccccc" }, fgColor = new { type = "string", pattern = "^#[0-9a-f]{6}$", @default = "#333333" }, useExactSize = BooleanSchema(true), customText = new { type = "string", maxLength = 256, @default = "" } }, ["width", "height"]),
        Tool("generate_qr_code", "Generate a QR code as SVG for text or Wi-Fi using KillerTools QR code logic. Wi-Fi credentials are part of the encoded output.", new { mode = EnumSchema("text", "wifi"), text = new { type = "string", minLength = 1, maxLength = 1024 }, wifi = WifiQrSchema(), foreground = new { type = "string", pattern = "^#[0-9a-f]{6}$", @default = "#000000" }, background = new { type = "string", pattern = "^#[0-9a-f]{6}$", @default = "#ffffff" }, errorCorrectionLevel = new { type = "string", @enum = new[] { "low", "medium", "quartile", "high" }, @default = "medium" } }, ["mode"]),
        Tool("calculate_chmod", "Calculate octal and symbolic Unix permissions.", new { permissions = PermissionSetSchema() }, ["permissions"]),
        Tool("expand_ipv4_range", "Find the smallest covering CIDR block for an IPv4 address range.", new { startIp = new { type = "string", maxLength = 15 }, endIp = new { type = "string", maxLength = 15 } }, ["startIp", "endIp"]),
        Tool("calculate_ipv4_subnet", "Calculate IPv4 network information.", new { address = new { type = "string", minLength = 1, maxLength = 32 } }, ["address"]),
        Tool("generate_ipv6_ula", "Generate IPv6 unique local address ranges from a MAC address.", new { macAddress = new { type = "string", pattern = "^([0-9a-f]{2}:){5}[0-9a-f]{2}$" }, timestampMs = new { type = "integer", minimum = 0L, maximum = 8_640_000_000_000_000L } }, ["macAddress"]),
        Tool("json_to_csv", "Convert an array of JSON objects to CSV.", new { rows = new { type = "array", minItems = 1, maxItems = 100, items = new { type = "object" } } }, ["rows"]),
        Tool("parse_url", "Parse a URL into its parts and query parameters.", new { url = StringSchema() }, ["url"]),
        Tool("parse_user_agent", "Parse a user agent string using KillerTools.", new { userAgent = new { type = "string", minLength = 1, maxLength = 1024 } }, ["userAgent"]),
        Tool("parse_phone_number", "Parse, validate, and format a phone number using KillerTools.", new { phone = new { type = "string", minLength = 1, maxLength = 64, pattern = "^[0-9 +\\-()]+$" }, defaultCountry = new { type = "string", minLength = 2, maxLength = 2 } }, ["phone"]),
        Tool("convert_color", "Convert a color into the formats shown by KillerTools Color Converter.", new { color = new { type = "string", minLength = 1, maxLength = 128 } }, ["color"]),
        Tool("lookup_mac_vendor", "Look up a MAC address prefix in the local KillerTools OUI catalog.", new { macAddress = new { type = "string", minLength = 6, maxLength = 24, pattern = "^[0-9a-fA-F.:-]+$" } }, ["macAddress"]),
        Tool("lookup_domain_dns", "Query DNS records through Cloudflare DNS, as used by KillerTools Domain Lookup.", new { name = new { type = "string", minLength = 4, maxLength = 253 }, type = EnumSchema("A", "AAAA", "MX", "TXT", "CNAME", "NS", "CAA", "SRV") }, ["name"]),
        Tool("lookup_domain_rdap", "Look up public domain registration details through RDAP, as used by KillerTools Domain Lookup.", new { domain = new { type = "string", minLength = 4, maxLength = 253 } }, ["domain"]),
        Tool("lookup_cve", "Look up a CVE ID or search terms through the same NVD proxy used by KillerTools. Returns bounded vulnerability summaries and named multi-CVE exploit chains.", new { query = new { type = "string", minLength = 2, maxLength = 80 } }, ["query"]),
        Tool("generate_spf_record", "Build an SPF TXT record.", new { providers = StringArraySchema(15), ipAddresses = StringArraySchema(20), enforcement = EnumSchema("-all", "~all", "?all") }, []),
        Tool("generate_dmarc_record", "Build a DMARC TXT record.", new { policy = EnumSchema("reject", "quarantine", "none"), subdomainPolicy = EnumSchema("reject", "quarantine", "none", ""), percentage = new { type = "integer", minimum = 0, maximum = 100, @default = 100 }, ruaEmails = StringArraySchema(5), rufEmails = StringArraySchema(5), adkim = EnumSchema("", "s"), aspf = EnumSchema("", "s") }, []),
        Tool("parse_email_headers", "Parse message headers, delivery hops, authentication results, and spam signals.", new { headers = new { type = "string", minLength = 1, maxLength = 16_384 } }, ["headers"]),
        Tool("test_regex", "Match text using safe RE2 syntax.", new { pattern = new { type = "string", minLength = 1, maxLength = 256 }, text = new { type = "string", maxLength = 4096 }, global = BooleanSchema(true), ignoreCase = BooleanSchema(false), multiline = BooleanSchema(false), dotAll = BooleanSchema(true) }, ["pattern", "text"]),
        Tool("encode_file_base64_local", "Read a local file and return a bounded Base64 representation.", new { path = new { type = "string", minLength = 1, maxLength = 1024 } }, ["path"]),
        Tool("decode_file_base64_local", "Decode Base64 to a new local file. Existing files are never overwritten.", new { path = new { type = "string", minLength = 1, maxLength = 1024 }, base64 = new { type = "string", minLength = 1, maxLength = 43_692 } }, ["path", "base64"]),
        Tool("check_pdf_signatures_local", "Read a local PDF and return signatures found by the KillerTools PDF signature reader.", new { path = new { type = "string", minLength = 1, maxLength = 1024 } }, ["path"]),
        Tool("otp_private", "Generate or verify a six-digit HOTP or TOTP code locally.", new { secret = new { type = "string", minLength = 8, maxLength = 128 }, mode = EnumSchema("totp", "hotp"), counter = new { type = "integer", minimum = 0L, maximum = 9_007_199_254_740_991L }, timeStep = new { type = "integer", minimum = 15, maximum = 300, @default = 30 }, code = new { type = "string", pattern = "^\\d{6}$" } }, ["secret"]),
        Tool("generate_otp_secret_private", "Generate a random Base32 OTP secret and an otpauth URI locally.", new { issuer = new { type = "string", minLength = 1, maxLength = 128, @default = "killer-tools" }, account = new { type = "string", minLength = 1, maxLength = 128, @default = "demo-user" } }, []),
        Tool("generate_ulids", "Generate up to 100 ULIDs.", new { count = new { type = "integer", minimum = 1, maximum = 100, @default = 1 } }, []),
        Tool("generate_uuids", "Generate up to 100 UUIDs.", new { version = EnumSchema("NIL", "v1", "v3", "v4", "v5"), count = new { type = "integer", minimum = 1, maximum = 100, @default = 1 }, name = new { type = "string", maxLength = 128 }, @namespace = new { type = "string", maxLength = 36 } }, []),
        Tool("generate_lorem_ipsum", "Generate bounded Lorem Ipsum text using the KillerTools generator.", new { paragraphCount = new { type = "integer", minimum = 1, maximum = 10, @default = 1 }, sentencePerParagraph = new { type = "integer", minimum = 1, maximum = 10, @default = 3 }, wordCount = new { type = "integer", minimum = 1, maximum = 20, @default = 10 }, startWithLoremIpsum = BooleanSchema(true), asHTML = BooleanSchema(false) }, []),
        Tool("parse_jwt_private", "Decode a JWT locally without claiming to verify its signature.", new { token = new { type = "string", minLength = 3, maxLength = 16_384 } }, ["token"]),
        Tool("generate_rsa_keypair_private", "Generate an RSA PEM key pair locally.", new { bits = EnumSchema("2048", "3072", "4096") }, []),
        Tool("crypt_text_private", "Encrypt or decrypt text locally with the KillerTools CryptoJS-compatible algorithms.", new { value = new { type = "string", maxLength = 8192 }, secret = new { type = "string", maxLength = 8192 }, algorithm = EnumSchema("AES", "TripleDES", "Rabbit", "RC4"), action = EnumSchema("encrypt", "decrypt") }, ["value", "secret", "action"]),
        Tool("hash_text_private", "Hash private text locally using the KillerTools hash algorithms.", new { value = new { type = "string", maxLength = 8192 }, algorithm = EnumSchema("MD5", "RIPEMD160", "SHA1", "SHA3", "SHA224", "SHA256", "SHA384", "SHA512"), encoding = EnumSchema("Bin", "Hex", "Base64", "Base64url") }, ["value"]),
        Tool("hmac_private", "Calculate an HMAC locally using the KillerTools hash algorithms.", new { value = new { type = "string", maxLength = 8192 }, secret = new { type = "string", maxLength = 8192 }, algorithm = EnumSchema("MD5", "RIPEMD160", "SHA1", "SHA3", "SHA224", "SHA256", "SHA384", "SHA512"), encoding = EnumSchema("Bin", "Hex", "Base64", "Base64url") }, ["value", "secret"]),
        Tool("bcrypt_private", "Hash or compare a string with bcrypt locally.", new { value = new { type = "string", maxLength = 8192 }, action = EnumSchema("hash", "compare"), rounds = new { type = "integer", minimum = 4, maximum = 14, @default = 10 }, hash = new { type = "string", maxLength = 128 } }, ["value", "action"]),
        Tool("bip39_private", "Generate a mnemonic or convert between entropy and a mnemonic locally using KillerTools BIP39.", new { entropy = new { type = "string", pattern = "^(?:[0-9a-f]{16}|[0-9a-f]{20}|[0-9a-f]{24}|[0-9a-f]{28}|[0-9a-f]{32})$" }, mnemonic = new { type = "string", minLength = 1, maxLength = 512 }, language = EnumSchema("English", "Czech", "French", "Italian", "Japanese", "Korean", "Portuguese", "Spanish", "Chinese simplified", "Chinese traditional") }, []),
        Tool("analyze_password_private", "Estimate password strength and crack time locally.", new { password = new { type = "string", maxLength = 8192 } }, ["password"]),
        Tool("generate_password_private", "Generate a password or passphrase locally with the KillerTools modes.", new { mode = EnumSchema("random", "passphrase", "pronounceable", "format"), length = new { type = "integer", minimum = 8, maximum = 128, @default = 20 }, uppercase = BooleanSchema(true), lowercase = BooleanSchema(true), numbers = BooleanSchema(true), symbols = BooleanSchema(true), excludeAmbiguous = BooleanSchema(true), requireOneOfEach = BooleanSchema(true), wordCount = new { type = "integer", minimum = 1, maximum = 16, @default = 6 }, wordSeparator = new { type = "string", maxLength = 8, @default = "-" }, capitalizeWords = BooleanSchema(true), appendNumber = BooleanSchema(false), format = EnumSchema("hex", "base64", "base64url", "uuid") }, []),
        Tool("evaluate_math", "Evaluate a bounded arithmetic expression.", new { expression = new { type = "string", minLength = 1, maxLength = 256 } }, ["expression"]),
        Tool("list_film_stocks", "List the film stocks available in KillerTools Reciprocity Calculator.", new { }, []),
        Tool("calculate_reciprocity", "Calculate reciprocity failure adjustment for a film stock.", new { filmStockId = new { type = "string", minLength = 1, maxLength = 64 }, meteredSeconds = new { type = "number", minimum = 0.0001, maximum = 3600 } }, ["filmStockId", "meteredSeconds"]),
        Tool("list_film_development_options", "List films and developers available in KillerTools Film Development Calculator.", new { }, []),
        Tool("calculate_film_development", "Calculate film development time and dilution volumes using KillerTools.", new { filmName = new { type = "string", minLength = 1, maxLength = 128 }, developerId = new { type = "string", minLength = 1, maxLength = 32 }, dilutionIndex = new { type = "integer", minimum = 0, maximum = 10 }, baseSeconds = new { type = "number", minimum = 1, maximum = 10_000 }, temperatureC = new { type = "number", minimum = 10, maximum = 42 }, pushPullStops = new { type = "number", minimum = -5, maximum = 5, @default = 0 }, tankMl = new { type = "number", minimum = 100, maximum = 2000, @default = 500 } }, ["filmName", "developerId"]),
        Tool("killermcp_update_status", "Check the installed KillerMCP version and whether a newer signed Windows installer is available.", new { }, []),
    ];

    private static readonly HashSet<string> CoreToolNames = new(CoreTools.Select(tool =>
        (string)tool.GetType().GetProperty("name")!.GetValue(tool)!), StringComparer.Ordinal);

    public static object[] Tools => CoreTools.Concat(Adapters.Values.Select(adapter => new
    {
        name = adapter.Tool.Name,
        description = adapter.Tool.Description,
        inputSchema = adapter.Tool.InputSchema,
        annotations = ToolSafety.For(adapter.Tool.Name),
    })).ToArray<object>();

    public static void SetAdapters(IEnumerable<AppAdapter> adapters) => Adapters = adapters.ToDictionary(adapter => adapter.Tool.Name, StringComparer.Ordinal);

    public static object Call(JsonElement parameters, JsonSerializerOptions json)
    {
        var name = parameters.GetProperty("name").GetString();
        var arguments = parameters.GetProperty("arguments");
        var loggedName = name is not null && (CoreToolNames.Contains(name) || Adapters.ContainsKey(name))
            ? name : "(unknown)";
        ToolCallLog.Record(loggedName, "started");
        try
        {
            if (name is not null && Adapters.TryGetValue(name, out var adapter))
            {
                var adapterResult = adapter.CallAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
                ToolCallLog.Record(loggedName, adapterResult.IsError ? "error" : "ok");
                return adapterResult.IsError
                    ? new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = true }
                    : new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = false };
            }

            object value = name switch
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
            "open_browser_companion_local" => new { url = Companion.Url },
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
            ToolCallLog.Record(loggedName, "ok");
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value, json) } } };
        }
        catch (ToolCallException exception)
        {
            ToolCallLog.Record(loggedName, "error");
            return new { content = new[] { new { type = "text", text = exception.Message } }, isError = true };
        }
    }

    private static object Tool(string name, string description, object properties, string[] required) =>
        new { name, description, inputSchema = new { type = "object", properties, required, additionalProperties = false }, annotations = ToolSafety.For(name) };
    private static object LookupTool(string name, string description) =>
        Tool(name, description, new { query = StringSchema(), limit = new { type = "integer", minimum = 1, maximum = 20, @default = 10 }, locale = EnumSchema("en", "bn", "cs", "de", "es", "fr", "hu", "it", "ja", "kk", "no", "pl", "pt", "ru", "tr", "uk", "vi", "zh", "zh-TW") }, ["query"]);
    private static object StringSchema() => new { type = "string", maxLength = 4096 };
    private static object StructuredTextSchema() => new { type = "string", minLength = 1, maxLength = 4096 };
    private static object BooleanSchema(bool defaultValue) => new { type = "boolean", @default = defaultValue };
    private static object IntegerSchema(int minimum, int maximum) => new { type = "integer", minimum, maximum };
    private static object NumberSchema() => new { type = "number" };
    private static object EnumSchema(params string[] values) => new { type = "string", @enum = values };
    private static object StringArraySchema(int maximum) => new { type = "array", maxItems = maximum, items = new { type = "string" }, @default = Array.Empty<string>() };
    private static object PermissionSetSchema()
    {
        var group = new { type = "object", properties = new { read = new { type = "boolean" }, write = new { type = "boolean" }, execute = new { type = "boolean" } }, required = new[] { "read", "write", "execute" }, additionalProperties = false };
        return new { type = "object", properties = new { owner = group, group, @public = group }, required = new[] { "owner", "group", "public" }, additionalProperties = false };
    }
    private static object WifiQrSchema() => new
    {
        type = "object",
        properties = new
        {
            ssid = new { type = "string", minLength = 1, maxLength = 128 },
            password = new { type = "string", maxLength = 128, @default = "" },
            encryption = new { type = "string", @enum = new[] { "nopass", "WPA", "WEP", "WPA2-EAP" }, @default = "WPA" },
            eapMethod = new { type = new[] { "string", "null" }, @enum = new string?[] { null, "MD5", "POTP", "GTC", "TLS", "IKEv2", "SIM", "AKA", "AKA'", "TTLS", "PWD", "LEAP", "PSK", "FAST", "TEAP", "EKE", "NOOB", "PEAP" }, @default = (string?)null },
            isHiddenSSID = BooleanSchema(false),
            eapAnonymous = BooleanSchema(false),
            eapIdentity = new { type = "string", maxLength = 128, @default = "" },
            eapPhase2Method = new { type = new[] { "string", "null" }, @enum = new string?[] { null, "None", "MSCHAPV2" }, @default = (string?)null },
        },
        required = new[] { "ssid" },
        additionalProperties = false,
    };
    private static bool OptionalBoolean(JsonElement arguments, string name, bool defaultValue = false) => arguments.TryGetProperty(name, out var value) ? value.GetBoolean() : defaultValue;
    private static string OptionalString(JsonElement arguments, string name, string defaultValue) => arguments.TryGetProperty(name, out var value) ? value.GetString()! : defaultValue;
    private static int OptionalInteger(JsonElement arguments, string name, int defaultValue) => arguments.TryGetProperty(name, out var value) ? value.GetInt32() : defaultValue;
    private static string[] StringArray(JsonElement arguments, string name) => arguments.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(value => value.GetString()!).ToArray() : [];
    private static string Query(JsonElement arguments) => arguments.GetProperty("query").GetString()!;
    private static int Limit(JsonElement arguments) => arguments.TryGetProperty("limit", out var value) ? value.GetInt32() : 10;
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
        try { return Companion.Get(type); }
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
    private static object Bcrypt(JsonElement arguments)
    {
        try
        {
            var value = arguments.GetProperty("value").GetString()!;
            if (arguments.GetProperty("action").GetString() == "compare")
            {
                if (!arguments.TryGetProperty("hash", out var hash) || string.IsNullOrEmpty(hash.GetString())) throw new ToolCallException("A bcrypt hash is required");
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

    private static string? OptionalNullableString(JsonElement arguments, string name) => arguments.TryGetProperty(name, out var value) ? value.GetString() : null;
}

sealed class ToolCallException(string message) : Exception(message);
