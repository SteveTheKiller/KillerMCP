# What KillerMCP can do

KillerMCP gives an MCP compatible agent one local connection to KillerTools and the supported Killer apps installed on the computer. People can ask in ordinary language. They do not need to remember internal operation names.

Examples:

```text
killer domain search thekiller.net
killer calculate 192.168.10.0/24
killer merge these PDFs
killerscan 192.168.8.0/24
killer find my notes about the office network
```

The agent chooses a matching available tool from the request and context. `killer` is usually enough. An app name such as `killerpdf`, `killerscan`, or `killernotes` can make the intended target explicit.

## Installed KillerMCP and the hosted server

The installed KillerMCP runtime includes all 81 KillerTools website utilities, plus tools supplied by supported Killer apps found on the computer. It runs as one local stdio MCP connection.

The optional public endpoint at `https://mcp.killertools.net` is a separate Cloudflare Worker. It offers 74 operations across 64 KillerTools utilities without an installation. The remaining 17 KillerTools utilities need local files, browser interaction, or private values, so they are available only through installed KillerMCP.

The public Worker does not provide desktop app tools and cannot reach local files, notes, calendars, or networks.

## KillerTools utilities

### Network, Windows, email, and technician references

- `domain-lookup`: Check DNS records and domain registration through RDAP.
- `ipv4-subnet-calculator`: Calculate IPv4 networks, host ranges, masks, and broadcast addresses.
- `ipv4-range-expander`: Expand a bounded IPv4 address range.
- `ipv6-ula-generator`: Generate an IPv6 unique local address prefix.
- `mac-address-lookup`: Identify a network adapter vendor from a MAC address.
- `port-protocol-reference`: Look up common TCP and UDP ports and protocols.
- `cve-lookup`: Look up CVEs by identifier or keyword using public vulnerability data.
- `windows-error-codes`: Explain Windows system error codes.
- `windows-event-lookup`: Look up Windows event identifiers.
- `group-policy-reference`: Find Group Policy settings and registry paths.
- `m365-sku-decoder`: Decode Microsoft 365 license SKU identifiers.
- `exchange-ndr-lookup`: Explain Exchange non-delivery report codes.
- `email-header-parser`: Parse mail routing and authentication headers.
- `email-record-generator`: Build SPF and DMARC records.
- `powershell-builder`: Search cmdlets, inspect parameters, and assemble a PowerShell command for review.
- `http-status-codes`: Explain HTTP status codes.
- `url-parser`: Break a URL into its components.
- `user-agent-parser`: Identify browser and platform information from a user agent string.
- `crontab-generator`: Explain or build a cron schedule.
- `chmod-calculator`: Convert Unix permissions between symbolic and numeric forms.

### Data conversion, formatting, and comparison

- `base64-string-converter`: Encode or decode Base64 text.
- `base64-file-converter`: Encode a local file or decode Base64 into a new local file.
- `case-converter`: Convert text among common letter case styles.
- `color-converter`: Convert colors among common formats.
- `date-time-converter`: Convert dates, times, and timestamps.
- `html-entities`: Escape or unescape HTML entities.
- `integer-base-converter`: Convert integers among binary, octal, decimal, and hexadecimal.
- `json-converter`: Convert JSON to or from YAML and TOML.
- `json-diff`: Compare two JSON values.
- `json-minify`: Remove unnecessary JSON whitespace.
- `json-to-csv`: Convert an array of JSON records to CSV.
- `json-viewer`: Validate and format JSON.
- `markdown-to-html`: Convert Markdown to HTML.
- `regex-tester`: Test a regular expression against text.
- `roman-numeral-converter`: Convert between Arabic numbers and Roman numerals.
- `sql-prettify`: Format SQL for readability.
- `temperature-converter`: Convert temperatures among common units.
- `text-diff`: Compare two text values.
- `text-statistics`: Count characters, words, lines, and other text statistics.
- `text-to-binary`: Convert text to ASCII binary or binary back to text.
- `text-to-nato-alphabet`: Spell text with the NATO phonetic alphabet.
- `toml-converter`: Convert or format TOML data.
- `xml-formatter`: Validate and format XML.
- `xml-json-converter`: Convert XML and JSON.
- `yaml-converter`: Convert or format YAML data.
- `yaml-viewer`: Validate and format YAML.
- `phone-parser-and-formatter`: Parse and format phone numbers.

### Generation and general utilities

- `ascii-text-drawer`: Render text as ASCII art.
- `emoji-picker`: Search emoji by name or keyword.
- `gif-search`: Search a public GIF provider.
- `lorem-ipsum-generator`: Generate placeholder prose.
- `math-evaluator`: Evaluate a bounded mathematical expression.
- `meta-tag-generator`: Generate common HTML metadata tags.
- `percentage-calculator`: Calculate percentage values and changes.
- `qr-code-generator`: Generate QR codes, including Wi-Fi payloads.
- `svg-placeholder-generator`: Generate an SVG image placeholder.
- `ulid-generator`: Generate ULIDs.
- `uuid-generator`: Generate UUIDs.
- `killer-modules`: Search the Killer Modules catalog.
- `killer-scripts`: Search the Killer Scripts catalog.

### Photography and film

- `depth-of-field-calculator`: Calculate depth of field and focus limits.
- `dev-calculator`: List film development options and calculate adjusted development times.
- `exposure-equivalence`: Calculate equivalent exposure settings.
- `nd-filter-calculator`: Calculate exposure changes for neutral density filters.
- `reciprocity-calculator`: Calculate long exposures with film reciprocity correction.

### Private and cryptographic tools

These tools run through installed KillerMCP and are not exposed by the public Cloudflare Worker.

- `bcrypt`: Hash or verify text with bcrypt.
- `bip39-generator`: Generate BIP39 mnemonic material locally.
- `encryption`: Encrypt or decrypt text locally.
- `hash-text`: Calculate cryptographic hashes.
- `hmac-generator`: Calculate an HMAC.
- `jwt-parser`: Decode a JWT locally. Decoding does not verify its signature.
- `otp-code-generator-and-validator`: Generate an OTP secret or calculate and validate codes.
- `password-generator`: Generate passwords locally.
- `password-strength-analyser`: Analyze password strength locally.
- `rsa-key-pair-generator`: Generate an RSA key pair locally.
- `pdf-signature-checker`: Inspect signatures in a local PDF without changing it.

### Browser and device interaction

Installed KillerMCP can open a token-protected browser companion on the local computer. The user provides or captures the value in that page, then the agent retrieves the latest result.

- `camera-recorder`: Retrieve a user-approved camera capture.
- `device-information`: Retrieve browser and device information shown by the companion.
- `html-wysiwyg-editor`: Retrieve HTML entered in the local editor.
- `keycode-info`: Retrieve the latest key event details.
- `signature-creator`: Retrieve a signature drawn in the local page.

Camera access begins only after the user enables it in the browser. Camera capture still needs final verification on physical hardware.

## Killer app tools

App tools appear only when KillerMCP finds a supported installed version that advertises the required command line capability.

### KillerPDF

- Merge 2 to 8 local PDF files into one new PDF while preserving the requested order.
- Refuse to overwrite an existing output file.
- Run general, attachment, or print preflight checks when the installed build supports them.
- Run an accessibility report when the installed build supports it.
- Limit each PDF to 64 MiB and combined merge inputs to 128 MiB.

### KillerScan

- Report the active local IPv4 interface, address, subnet, gateway, and DNS server without scanning other hosts.
- Run a quick discovery scan against one IPv4 address or a CIDR containing no more than 1,024 addresses.
- Run a full scan with fingerprinting and port checks when explicitly requested.
- Return no more than 100 discovered devices.

Network scans send probes from the local computer. Only scan networks that the user is authorized to test.

### KillerShell

- Search one absolute local folder by filename text or wildcard pattern.
- Search file contents for a text value.
- Combine filename and content filters.
- Return no more than 100 results without changing files.

### KillerNotes

- Search the active local notes database by title, tag, or text.
- Return bounded text snippets without changing notes.
- Accept a query up to 200 characters and return no more than 20 matches.

Encrypted notes databases cannot currently be unlocked through MCP.

### Killendar

- Read the active local calendar starting from a requested date.
- Expand recurring appointments that fall inside the requested range.
- Read up to 31 days and return no more than 100 events.
- Keep the calendar read-only. The tool cannot create, edit, move, or delete appointments.

Password-protected calendars cannot currently be unlocked through MCP.

## Where data goes

Installed KillerMCP runs its local tools and app adapters on the computer. Local app data and files are not sent to the public Cloudflare Worker. Some KillerTools network and reference lookups still contact their named public data sources.

The MCP client and its model provider can receive tool arguments and results, including local paths, snippets, calendar fields, network results, or private values. The provider's data handling rules still apply. Review sensitive inputs and requested paths before approving a tool call.

Requests to the hosted endpoint are processed by the KillerTools Cloudflare Worker. It has no sign-in, rejects request bodies over 64 KiB, applies bounded input schemas, and rate limits requests by connecting IP. Do not send passwords, private files, tokens, or client data to the public endpoint.

## Source and exact operation names

- [KillerMCP](https://github.com/SteveTheKiller/KillerMCP) contains the shared local runtime, app adapters, installer, and integration checks.
- [KillerTools](https://github.com/SteveTheKiller/killer-tools-site) contains the 81 website utilities, public Cloudflare Worker, local KillerTools bundle, and coverage map.
- The exact internal operation mapping is maintained in `mcp/coverage.json` in the KillerTools repository.

This document describes the current development implementation. The signed public KillerMCP installer has not been released yet.
