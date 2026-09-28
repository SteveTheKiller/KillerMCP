# What KillerMCP can do

KillerMCP gives an MCP compatible agent one local connection to KillerTools and the supported Killer apps installed on the computer. People can ask in ordinary language. They do not need to remember internal operation names.

Examples:

```text
killer domain search thekiller.net
killer calculate 192.168.10.0/24
killer merge these PDFs
killerscan my network
killer find my notes about the office network
```

The agent chooses a matching available tool from the request and context. `killer` is usually enough. An app name such as `killerpdf`, `killerscan`, or `killernotes` can make the intended target explicit.

## How KillerMCP works

The installed native .NET 10 host includes all 81 KillerTools website utilities from the pinned KillerTools engine, plus tools supplied by supported Killer apps found on the computer. It runs as one local stdio MCP connection.

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

These tools run locally through installed KillerMCP.

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

### Cross app workflows

- Create a PDF from HTML or plain text generated by an agent.
- Export a KillerNotes note directly to PDF.
- Scan a network and create a PDF report with a device table.
- Save a KillerScan device report as a KillerNotes note.
- Export a Killendar agenda to PDF or save it as a note.
- Export a KillerShell directory listing to PDF or save it as a note.
- Render selected KillerPDF pages and save each page image as a new rich note.

PDF creation uses the installed Microsoft Edge print engine. Output paths must be absolute, their parent folder must already exist, and existing files are never overwritten.

### KillerPDF

- Merge 2 to 8 local PDF files into one new PDF while preserving the requested order.
- Extract selected pages into a new PDF or split every page into separate PDFs.
- Remove encryption into a new PDF, using a supplied password when required.
- Render selected pages as PNG or JPEG images.
- Flatten a PDF into a new uneditable raster PDF.
- Print selected pages to a named or default printer.
- Add a searchable OCR text layer, downloading the selected language model on first use when needed.
- Resave a PDF through KillerPDF's standard pipeline.
- Render bounded benchmark images with timing data.
- Rotate, delete, move, insert, or duplicate pages into a new PDF when the installed KillerPDF version supports those commands.
- Read document details or search PDF text without changing the file when the installed KillerPDF version supports those commands.
- Run general, attachment, or print preflight checks when the installed build supports them.
- Run an accessibility report when the installed build supports it.
- Refuse to overwrite existing files or output folders.
- Limit each PDF to 64 MiB and combined merge inputs to 128 MiB.

### KillerScan

- Report the active local IPv4 interface, address, subnet, gateway, and DNS server without scanning other hosts.
- Run a quick discovery scan against one IPv4 address or a CIDR containing no more than 1,024 addresses.
- Run a full scan with fingerprinting and port checks when explicitly requested.
- Deep probe one IPv4 host, including ports 1 through 1024.
- Look up a MAC address manufacturer in KillerScan's offline OUI database.
- Ping one IPv4 address or hostname and report latency and packet loss.
- Trace the route to one IPv4 address or hostname.
- Diagnose DNS, ping, route selection, and selected TCP ports for one target.
- Watch up to 16 IPv4 addresses for bounded availability and latency samples.
- Run KillerScan's native KillerSpeed test when explicitly requested.
- Return no more than 100 discovered devices.

Network tools send probes from the local computer. Only test networks that the user is authorized to test. KillerSpeed contacts speed.killerscan.net and can transfer up to 6 GiB of generated test data plus network overhead.

### KillerShell

- Search one absolute local folder by filename text or wildcard pattern.
- Search file contents for a text value.
- Combine filename and content filters.
- List up to 100 files and folders directly inside one absolute directory.
- Read file or folder size, timestamps, type, and attributes.
- Read up to 32,768 characters from one text file.
- Calculate the SHA-256 hash of one local file.
- List up to 100 running processes with process IDs and memory use.
- List up to 100 Windows services with their current status.
- Read up to 100 recent Application, System, or Security event log entries.
- List bounded subkeys and values from one registry key.
- Report local drive type, readiness, capacity, and free space.
- Return bounded results without changing files or Windows settings.

All KillerShell MCP tools are read-only. They cannot run terminal commands, start or stop processes, control services, change the registry, clear event logs, or create, edit, move, rename, or delete files. Returned data can include local paths, file text, process and service names, event messages, registry values, drive information, and file hashes. The MCP client and its model provider can receive those results.

### KillerNotes

- Search note titles, tags, and Markdown content.
- List notes by group or tag, read a note by ID, and inspect groups, tags, backlinks, outgoing links, history, and vault statistics.
- Create Markdown notes and update their title, content, group, tags, or title color.
- Create nested groups and set or clear group and note title colors.
- Save a local image as a new rich note.
- Export a note to a new TXT, Markdown, HTML, or KNOTE file. The resulting path can be passed to another installed Killer app.
- Bound query, result, and content sizes before KillerNotes receives the request.

Encrypted notes databases cannot currently be unlocked through MCP.

### Killendar

- Read the active local calendar starting from a requested date.
- Expand recurring appointments that fall inside the requested range.
- Read up to 31 days and return no more than 100 events.
- Create a single timed or all-day appointment in the open, unlocked Killendar. The app saves it and updates the visible calendar.

Agenda lookup cannot currently unlock a password-protected calendar. Appointment creation uses the running app after it has been unlocked. Editing, deleting, and creating recurring appointments remain in the app interface.

## Where data goes

Installed KillerMCP runs its tools and app adapters on the computer. Some KillerTools network and reference lookups still contact their named public data sources.

The MCP client and its model provider can receive tool arguments and results, including local paths, snippets, calendar fields, network results, or private values. The provider's data handling rules still apply. Review sensitive inputs and requested paths before approving a tool call.

## Source and exact operation names

- [KillerMCP](https://github.com/SteveTheKiller/KillerMCP) contains the native host, app adapters, client registration, installer, and integration checks.
- [KillerTools](https://github.com/SteveTheKiller/KillerTools) contains the native engine for the 81 website utilities, parity checks, and migration inventory.

The signed public installer is available from [KillerMCP releases](https://github.com/SteveTheKiller/KillerMCP/releases). App tools appear only when the installed app advertises the required command.
