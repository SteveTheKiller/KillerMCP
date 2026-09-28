import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { brotliDecompressSync } from 'node:zlib';

const updateDirectory = await mkdtemp(join(tmpdir(), 'killermcp-update-'));
const pdfSourceOne = join(updateDirectory, 'source-one.pdf');
const pdfSourceTwo = join(updateDirectory, 'source-two.pdf');
const signedPdf = join(updateDirectory, 'signed.pdf');
await writeFile(pdfSourceOne, 'fixture one');
await writeFile(pdfSourceTwo, 'fixture two');
await writeFile(signedPdf, brotliDecompressSync(Buffer.from(await readFile(resolve('dependencies/KillerTools/tests/KillerTools.Engine.ParityHost/Data/signed-pdf.br64'), 'utf8'), 'base64')));
let updateRequests = 0;
const updateServer = createServer((request, response) => {
  updateRequests++;
  response.writeHead(200, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify({ tag_name: 'v0.3.1' }));
});
await new Promise(resolve => updateServer.listen(0, '127.0.0.1', resolve));
const updateAddress = updateServer.address();
const environment = {
  ...process.env,
  KILLERMCP_UPDATE_API: `http://127.0.0.1:${updateAddress.port}/latest`,
  KILLERMCP_UPDATE_CACHE: join(updateDirectory, 'status.json'),
  KILLERNOTES_CLI: resolve('tests/KillerMCP.Runtime.Checks/bin/Release/net10.0/KillerMCP.Runtime.Checks.exe'),
  KILLERMCP_FAKE_NOTES: '1',
  KILLERMCP_FAKE_KILLENDAR: '1',
  KILLERMCP_FAKE_SHELL: '1',
  KILLERMCP_FAKE_SCAN: '1',
  KILLERMCP_FAKE_PDF: '1',
  KILLERPDF_CLI: resolve('tests/KillerMCP.Runtime.Checks/bin/Release/net10.0/KillerMCP.Runtime.Checks.exe'),
  KILLERSCAN_CLI: resolve('tests/KillerMCP.Runtime.Checks/bin/Release/net10.0/KillerMCP.Runtime.Checks.exe'),
  KILLERSHELL_CLI: resolve('tests/KillerMCP.Runtime.Checks/bin/Release/net10.0/KillerMCP.Runtime.Checks.exe'),
  KILLENDAR_CLI: resolve('tests/KillerMCP.Runtime.Checks/bin/Release/net10.0/KillerMCP.Runtime.Checks.exe'),
};
const child = spawn('dotnet', ['run', '--project', 'src/KillerMCP/KillerMCP.csproj', '-c', 'Release', '--no-build'], { stdio: ['pipe', 'pipe', 'inherit'], env: environment });
let buffer = '';
const pending = new Map();
child.stdout.setEncoding('utf8');
child.stdout.on('data', chunk => {
  buffer += chunk;
  while (buffer.includes('\n')) {
    const end = buffer.indexOf('\n');
    const message = JSON.parse(buffer.slice(0, end));
    buffer = buffer.slice(end + 1);
    pending.get(message.id)?.(message.result);
    pending.delete(message.id);
  }
});
let id = 0;
function request(method, params = {}) {
  const requestId = ++id;
  return new Promise(resolve => {
    pending.set(requestId, resolve);
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id: requestId, method, params })}\n`);
  });
}

const initialized = await request('initialize', { protocolVersion: '2025-06-18' });
assert.equal(initialized.serverInfo.name, 'KillerMCP');
assert.equal(initialized.serverInfo.version, '0.3.0');
assert.match(initialized.instructions, /KillerMCP 0\.3\.1 is available/);
const listed = await request('tools/list');
assert.deepEqual(listed.tools.map(tool => tool.name), [
  'text_statistics', 'convert_case', 'encode_base64', 'decode_base64', 'text_to_ascii_binary',
  'ascii_binary_to_text', 'draw_ascii_text', 'search_emoji', 'arabic_to_roman', 'roman_to_arabic', 'text_to_nato_alphabet',
  'convert_integer_base', 'convert_temperature', 'calculate_percentage', 'escape_html_entities',
  'unescape_html_entities', 'markdown_to_html', 'format_sql',
  'convert_date_time', 'lookup_exchange_ndr', 'lookup_group_policy', 'lookup_http_status',
  'lookup_m365_sku', 'lookup_port_protocol', 'lookup_windows_error', 'lookup_windows_event',
  'list_killer_modules', 'list_killer_scripts',
  'search_powershell_cmdlets', 'get_powershell_cmdlet', 'build_powershell_command',
  'diff_text', 'format_json', 'minify_json', 'diff_json', 'convert_xml_json', 'format_xml',
  'convert_json', 'convert_yaml', 'convert_toml', 'format_yaml', 'generate_meta_tags', 'search_gifs',
  'open_browser_companion_local', 'get_browser_device_information_local', 'get_browser_keycode_local',
  'get_browser_html_local', 'get_browser_signature_local', 'get_browser_camera_local',
  'describe_cron', 'calculate_nd_exposure', 'calculate_exposure_equivalence',
  'calculate_depth_of_field', 'generate_svg_placeholder', 'generate_qr_code', 'calculate_chmod',
  'expand_ipv4_range', 'calculate_ipv4_subnet', 'generate_ipv6_ula', 'json_to_csv',
  'parse_url', 'parse_user_agent', 'parse_phone_number', 'convert_color', 'lookup_mac_vendor', 'lookup_domain_dns', 'lookup_domain_rdap', 'lookup_cve', 'generate_spf_record', 'generate_dmarc_record', 'parse_email_headers',
  'test_regex',
  'encode_file_base64_local', 'decode_file_base64_local', 'check_pdf_signatures_local',
  'otp_private', 'generate_otp_secret_private',
  'generate_ulids', 'generate_uuids', 'generate_lorem_ipsum',
  'parse_jwt_private',
  'generate_rsa_keypair_private', 'crypt_text_private', 'hash_text_private', 'hmac_private', 'bcrypt_private', 'bip39_private',
  'analyze_password_private', 'generate_password_private',
  'evaluate_math',
  'list_film_stocks', 'calculate_reciprocity',
  'list_film_development_options', 'calculate_film_development',
  'killermcp_update_status',
  'killernotes_search', 'killernotes_list', 'killernotes_get', 'killernotes_groups',
  'killernotes_tags', 'killernotes_backlinks', 'killernotes_links', 'killernotes_history',
  'killernotes_stats', 'killernotes_create', 'killernotes_update', 'killernotes_create_group',
  'killernotes_set_group_color', 'killernotes_set_title_color', 'killernotes_import_image',
  'killernotes_export',
  'killendar_agenda', 'killendar_create_appointment',
  'killershell_search_files', 'killershell_list_directory', 'killershell_file_info', 'killershell_read_text_file',
  'killershell_list_processes', 'killershell_list_services', 'killershell_read_event_log',
  'killershell_read_registry_key', 'killershell_list_drives', 'killershell_hash_file',
  'killerscan_local_network', 'killerscan_scan_network', 'killerscan_probe_host',
  'killerscan_mac_vendor', 'killerscan_ping', 'killerscan_trace_route',
  'killerscan_diagnose_host', 'killerscan_watch_hosts', 'killerscan_speed_test',
  'killerpdf_merge', 'killerpdf_extract_pages', 'killerpdf_split', 'killerpdf_decrypt',
  'killerpdf_render_pages', 'killerpdf_flatten', 'killerpdf_print', 'killerpdf_ocr',
  'killerpdf_resave', 'killerpdf_benchmark_render', 'killerpdf_rotate_pages',
  'killerpdf_delete_pages', 'killerpdf_move_pages', 'killerpdf_insert_blank_page',
  'killerpdf_duplicate_page', 'killerpdf_document_info', 'killerpdf_search_text',
  'killerpdf_preflight', 'killerpdf_accessibility',
  'killer_create_pdf', 'killernotes_export_pdf', 'killerscan_export_report_pdf',
  'killerscan_save_report_note', 'killendar_export_agenda_pdf', 'killendar_save_agenda_note',
  'killershell_export_directory_pdf', 'killershell_save_directory_note', 'killerpdf_save_pages_as_notes',
]);
const called = await request('tools/call', { name: 'text_statistics', arguments: { text: 'hello world' } });
assert.deepEqual(JSON.parse(called.content[0].text), { characterCount: 11, wordCount: 2, lineCount: 1, byteSize: 11 });
const encoded = await request('tools/call', { name: 'encode_base64', arguments: { text: 'hé' } });
assert.deepEqual(JSON.parse(encoded.content[0].text), { encoded: 'aMOp' });
const asciiArt = await request('tools/call', { name: 'draw_ascii_text', arguments: { text: 'Killer', font: 'Circle', width: 20 } });
assert.deepEqual(JSON.parse(asciiArt.content[0].text), { art: 'Ⓚⓘⓛⓛⓔⓡ' });
const emoji = await request('tools/call', { name: 'search_emoji', arguments: { query: 'grinning face', limit: 1 } });
assert.equal(JSON.parse(emoji.content[0].text)[0].emoji, '😀');
const percentage = await request('tools/call', { name: 'calculate_percentage', arguments: { mode: 'change', x: 10, y: 12 } });
assert.deepEqual(JSON.parse(percentage.content[0].text), { value: '+20%' });
const statuses = await request('tools/call', { name: 'lookup_http_status', arguments: { query: 'not found' } });
assert.equal(JSON.parse(statuses.content[0].text)[0].code, 404);
const policies = await request('tools/call', { name: 'lookup_group_policy', arguments: { query: 'minimum password length', limit: 1 } });
assert.equal(JSON.parse(policies.content[0].text)[0].name, 'Minimum Password Length');
const modules = await request('tools/call', { name: 'list_killer_modules', arguments: { name: 'pivot' } });
assert.equal(JSON.parse(modules.content[0].text)[0].name, 'KillerPivot');
const scripts = await request('tools/call', { name: 'list_killer_scripts', arguments: { query: 'AMORT' } });
assert.equal(JSON.parse(scripts.content[0].text)[0].filename, 'AMORT.ps1');
const cmdlets = JSON.parse((await request('tools/call', { name: 'search_powershell_cmdlets', arguments: { query: 'ActiveDirectory', limit: 20 } })).content[0].text);
assert.equal(cmdlets.some(item => item.cmdlet === 'Get-ADUser'), true);
const cmdlet = JSON.parse((await request('tools/call', { name: 'get_powershell_cmdlet', arguments: { cmdlet: 'get-aduser' } })).content[0].text);
assert.equal(cmdlet.cmdlet, 'Get-ADUser');
const builtCommand = JSON.parse((await request('tools/call', { name: 'build_powershell_command', arguments: { cmdlet: 'Get-ADUser', parameters: { Identity: "O'Brien", Properties: 'DisplayName, mail' } } })).content[0].text);
assert.equal(builtCommand.command, "Get-ADUser -Identity 'O''Brien' -Properties 'DisplayName','mail'");
const vietnameseNdr = await request('tools/call', { name: 'lookup_exchange_ndr', arguments: { query: 'SPF Validation Failed', limit: 1, locale: 'vi' } });
assert.equal(JSON.parse(vietnameseNdr.content[0].text)[0].name, 'Xác thực SPF không thành công');
const exposure = await request('tools/call', { name: 'calculate_nd_exposure', arguments: { baseSeconds: 0.5, stops: 6 } });
assert.deepEqual(JSON.parse(exposure.content[0].text), { seconds: 32 });
const difference = await request('tools/call', { name: 'diff_text', arguments: { left: 'one\ntwo', right: 'one\nnew' } });
assert.equal(JSON.parse(difference.content[0].text).added, 1);
const formattedSql = await request('tools/call', { name: 'format_sql', arguments: { sql: 'select a,b from t where x=1' } });
assert.equal(JSON.parse(formattedSql.content[0].text).text, 'SELECT\n  a,\n  b\nFROM\n  t\nWHERE\n  x = 1');
const formattedJson = await request('tools/call', { name: 'format_json', arguments: { text: '{b:2,a:1,}' } });
assert.equal(JSON.parse(formattedJson.content[0].text).text, '{\n   "a": 1,\n   "b": 2\n}');
const invalidJson = await request('tools/call', { name: 'minify_json', arguments: { text: '{broken:' } });
assert.equal(invalidJson.isError, true);
const parsedUserAgent = JSON.parse((await request('tools/call', { name: 'parse_user_agent', arguments: { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36' } })).content[0].text);
assert.equal(parsedUserAgent.browser.name, 'Chrome');
assert.equal(parsedUserAgent.engine.name, 'Blink');
const parsedPhone = JSON.parse((await request('tools/call', { name: 'parse_phone_number', arguments: { phone: '+1 415 555 2671' } })).content[0].text);
assert.equal(parsedPhone.countryCode, 'US');
assert.equal(parsedPhone.e164, '+14155552671');
const convertedColor = JSON.parse((await request('tools/call', { name: 'convert_color', arguments: { color: '#1ea54c' } })).content[0].text);
assert.equal(convertedColor.rgb, 'rgb(30, 165, 76)');
const macVendor = JSON.parse((await request('tools/call', { name: 'lookup_mac_vendor', arguments: { macAddress: '00:00:0c:12:34:56' } })).content[0].text);
assert.equal(macVendor.prefix, '00000C');
assert.match(macVendor.details, /^Cisco Systems, Inc/);
const qrCode = JSON.parse((await request('tools/call', { name: 'generate_qr_code', arguments: { mode: 'text', text: 'https://killertools.net' } })).content[0].text);
assert.match(qrCode.svg, /^<svg/);
assert.match(qrCode.svg, /width="256"/);
const wifiQr = await request('tools/call', { name: 'generate_qr_code', arguments: { mode: 'wifi', wifi: { ssid: 'Example', password: 'secret', encryption: 'WPA' } } });
assert.equal(wifiQr.isError, undefined);
const cve = JSON.parse((await request('tools/call', { name: 'lookup_cve', arguments: { query: 'CVE-2021-44228' } })).content[0].text);
assert.equal(cve.results[0].id, 'CVE-2021-44228');
const xmlJson = await request('tools/call', { name: 'convert_xml_json', arguments: { text: '<a x="1"/>', direction: 'xml_to_json' } });
assert.match(JSON.parse(xmlJson.content[0].text).text, /"_attributes"/);
const formattedXml = await request('tools/call', { name: 'format_xml', arguments: { text: '<a><b>text</b></a>' } });
assert.equal(JSON.parse(formattedXml.content[0].text).text, '<a>\n  <b>text</b>\n</a>');
const yamlJson = await request('tools/call', { name: 'convert_yaml', arguments: { text: 'a: 1', to: 'json' } });
assert.equal(JSON.parse(yamlJson.content[0].text).text, '{\n  "a": 1\n}');
const formattedYaml = await request('tools/call', { name: 'format_yaml', arguments: { text: 'b: 2\na: 1', sortKeys: true } });
assert.equal(JSON.parse(formattedYaml.content[0].text).text, 'a: 1\nb: 2\n');
const metaTags = await request('tools/call', { name: 'generate_meta_tags', arguments: { fields: { title: 'KillerTools' } } });
assert.match(JSON.parse(metaTags.content[0].text).html, /property="og:title" value="KillerTools"/);
const invalidMetaTags = await request('tools/call', { name: 'generate_meta_tags', arguments: { fields: { unknown: 'value' } } });
assert.equal(invalidMetaTags.isError, true);
assert.equal(invalidMetaTags.content[0].text, 'Unknown metadata field for selected page type');
const missingBrowserState = await request('tools/call', { name: 'get_browser_device_information_local', arguments: {} });
assert.equal(missingBrowserState.isError, true);
const companion = JSON.parse((await request('tools/call', { name: 'open_browser_companion_local', arguments: {} })).content[0].text);
const companionPage = await fetch(companion.url);
assert.equal(companionPage.status, 200);
assert.match(await companionPage.text(), /KillerTools MCP Browser Companion/);
const companionUrl = new URL(companion.url);
for (const [type, tool, value] of [
  ['device', 'get_browser_device_information_local', '{"userAgent":"test"}'],
  ['key', 'get_browser_keycode_local', '{"key":"A"}'],
  ['html', 'get_browser_html_local', '<p>test</p>'],
  ['signature', 'get_browser_signature_local', 'data:image/png;base64,dGVzdA=='],
  ['camera', 'get_browser_camera_local', 'data:image/png;base64,dGVzdA=='],
]) {
  const stateResponse = await fetch(new URL(`/state${companionUrl.search}`, companionUrl), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Origin: companionUrl.origin },
    body: JSON.stringify({ type, value }),
  });
  assert.equal(stateResponse.status, 204);
  const browserState = JSON.parse((await request('tools/call', { name: tool, arguments: {} })).content[0].text);
  assert.equal(browserState.value, value);
  assert.equal(typeof browserState.updatedAt, 'number');
}
const cron = await request('tools/call', { name: 'describe_cron', arguments: { expression: '*/5 * * * *' } });
assert.equal(JSON.parse(cron.content[0].text).description, 'Every 5 minutes, every hour, every day');
const subnet = await request('tools/call', { name: 'calculate_ipv4_subnet', arguments: { address: '198.51.100.27/24' } });
assert.equal(JSON.parse(subnet.content[0].text).cidr, '198.51.100.0/24');
const spf = await request('tools/call', { name: 'generate_spf_record', arguments: { providers: ['include:_spf.google.com'], ipAddresses: ['198.51.100.7'], enforcement: '-all' } });
assert.deepEqual(JSON.parse(spf.content[0].text), { record: 'v=spf1 include:_spf.google.com ip4:198.51.100.7 -all' });
const regex = await request('tools/call', { name: 'test_regex', arguments: { pattern: 'a+', text: 'caaab' } });
assert.equal(JSON.parse(regex.content[0].text).matches.length, 1);
const pdfSignatures = JSON.parse((await request('tools/call', { name: 'check_pdf_signatures_local', arguments: { path: signedPdf } })).content[0].text);
assert.equal(pdfSignatures.signatures.length, 1);
assert.equal(pdfSignatures.signatures[0].integrity, true);
const directory = await mkdtemp(join(tmpdir(), 'killermcp-'));
try {
  const source = join(directory, 'source.bin');
  const destination = join(directory, 'decoded.bin');
  await writeFile(source, Buffer.from([0, 1, 2, 254, 255]));
  const fileEncoded = await request('tools/call', { name: 'encode_file_base64_local', arguments: { path: source } });
  assert.deepEqual(JSON.parse(fileEncoded.content[0].text), { base64: 'AAEC/v8=', byteLength: 5 });
  const fileDecoded = await request('tools/call', { name: 'decode_file_base64_local', arguments: { path: destination, base64: 'AAEC/v8=' } });
  assert.equal(JSON.parse(fileDecoded.content[0].text).byteLength, 5);
  assert.deepEqual(await readFile(destination), Buffer.from([0, 1, 2, 254, 255]));
  const overwrite = await request('tools/call', { name: 'decode_file_base64_local', arguments: { path: destination, base64: 'AAEC/v8=' } });
  assert.equal(overwrite.isError, true);
} finally {
  await rm(directory, { recursive: true, force: true });
}
const hotp = await request('tools/call', { name: 'otp_private', arguments: { secret: 'GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ', mode: 'hotp', counter: 0 } });
assert.deepEqual(JSON.parse(hotp.content[0].text), { code: '755224', counter: 0 });
const otpSecret = await request('tools/call', { name: 'generate_otp_secret_private', arguments: { issuer: 'Killer Tools', account: 'user@example.com' } });
const generatedOtp = JSON.parse(otpSecret.content[0].text);
assert.match(generatedOtp.secret, /^[A-Z2-7]{16}$/);
assert.match(generatedOtp.uri, /^otpauth:\/\/totp\/Killer%20Tools:user%40example\.com\?/);
const ulids = await request('tools/call', { name: 'generate_ulids', arguments: { count: 2 } });
assert.match(JSON.parse(ulids.content[0].text).ids[0], /^[0-9A-HJKMNP-TV-Z]{26}$/);
const uuids = await request('tools/call', { name: 'generate_uuids', arguments: { version: 'v5', count: 1, name: 'example.com', namespace: '6ba7b810-9dad-11d1-80b4-00c04fd430c8' } });
assert.deepEqual(JSON.parse(uuids.content[0].text).ids, ['cfbff0d1-9375-5685-968c-48ce8b15ae17']);
const lorem = await request('tools/call', { name: 'generate_lorem_ipsum', arguments: { paragraphCount: 1, sentencePerParagraph: 1, wordCount: 4 } });
assert.equal(JSON.parse(lorem.content[0].text).text, 'Lorem ipsum dolor sit amet, consectetur adipiscing elit.');
const jwt = await request('tools/call', { name: 'parse_jwt_private', arguments: { token: 'eyJhbGciOiJub25lIn0.eyJzdWIiOiJsb2NhbCJ9.x' } });
assert.equal(JSON.parse(jwt.content[0].text).signatureVerified, false);
for (const algorithm of ['AES', 'TripleDES', 'Rabbit', 'RC4']) {
  const encrypted = JSON.parse((await request('tools/call', { name: 'crypt_text_private', arguments: { value: 'private π', secret: 'secret', algorithm, action: 'encrypt' } })).content[0].text).output;
  const decrypted = JSON.parse((await request('tools/call', { name: 'crypt_text_private', arguments: { value: encrypted, secret: 'secret', algorithm, action: 'decrypt' } })).content[0].text).output;
  assert.equal(decrypted, 'private π');
}
const hash = await request('tools/call', { name: 'hash_text_private', arguments: { value: 'abc' } });
assert.equal(JSON.parse(hash.content[0].text).hash, 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
const bcryptHash = await request('tools/call', { name: 'bcrypt_private', arguments: { value: 'private', action: 'hash', rounds: 4 } });
const generatedPassword = JSON.parse((await request('tools/call', { name: 'generate_password_private', arguments: { mode: 'random', length: 32 } })).content[0].text).password;
assert.equal(generatedPassword.length, 32);
assert.match(generatedPassword, /[A-Z]/);
assert.match(generatedPassword, /[a-z]/);
assert.match(generatedPassword, /[0-9]/);
const generatedPassphrase = JSON.parse((await request('tools/call', { name: 'generate_password_private', arguments: { mode: 'passphrase', wordCount: 6, appendNumber: true } })).content[0].text).password;
assert.equal(generatedPassphrase.split('-').length, 7);
const formattedPassword = JSON.parse((await request('tools/call', { name: 'generate_password_private', arguments: { mode: 'format', format: 'base64url', length: 24 } })).content[0].text).password;
assert.match(formattedPassword, /^[A-Za-z0-9_-]{24}$/);
const japaneseMnemonic = JSON.parse((await request('tools/call', { name: 'bip39_private', arguments: { entropy: '0000000000000000', language: 'Japanese' } })).content[0].text);
assert.equal(japaneseMnemonic.mnemonic, 'あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいだ');
const japaneseEntropy = JSON.parse((await request('tools/call', { name: 'bip39_private', arguments: { mnemonic: japaneseMnemonic.mnemonic, language: 'Japanese' } })).content[0].text);
assert.equal(japaneseEntropy.entropy, '0000000000000000');
const bcryptCompare = await request('tools/call', { name: 'bcrypt_private', arguments: { value: 'private', action: 'compare', hash: JSON.parse(bcryptHash.content[0].text).hash } });
assert.equal(JSON.parse(bcryptCompare.content[0].text).matches, true);
const rsa = await request('tools/call', { name: 'generate_rsa_keypair_private', arguments: { bits: '2048' } });
const rsaPair = JSON.parse(rsa.content[0].text);
assert.match(rsaPair.publicKeyPem, /BEGIN PUBLIC KEY/);
assert.match(rsaPair.privateKeyPem, /BEGIN RSA PRIVATE KEY/);
const strength = await request('tools/call', { name: 'analyze_password_private', arguments: { password: 'Secret123!' } });
assert.equal(JSON.parse(strength.content[0].text).charsetLength, 94);
const math = await request('tools/call', { name: 'evaluate_math', arguments: { expression: 'sqrt(81) + max(2, 5)' } });
assert.equal(JSON.parse(math.content[0].text).value, 14);
const stocks = await request('tools/call', { name: 'list_film_stocks', arguments: {} });
assert.equal(JSON.parse(stocks.content[0].text).length, 41);
const reciprocity = await request('tools/call', { name: 'calculate_reciprocity', arguments: { filmStockId: 'fuji-acros100ii', meteredSeconds: 120 } });
assert.equal(JSON.parse(reciprocity.content[0].text).noFailure, true);
const developmentOptions = await request('tools/call', { name: 'list_film_development_options', arguments: {} });
assert.ok(JSON.parse(developmentOptions.content[0].text).developers.some(developer => developer.id === 'd76'));
const development = await request('tools/call', { name: 'calculate_film_development', arguments: { filmName: 'Ilford HP5 Plus', developerId: 'd76' } });
assert.equal(JSON.parse(development.content[0].text).displayTime, '6m 30s');
const update = await request('tools/call', { name: 'killermcp_update_status', arguments: {} });
assert.equal(JSON.parse(update.content[0].text).updateAvailable, true);
assert.equal(updateRequests, 1);
const invalidUpdate = await request('tools/call', { name: 'killermcp_update_status', arguments: { force: true } });
assert.equal(invalidUpdate.isError, true);
const notes = await request('tools/call', { name: 'killernotes_search', arguments: { query: 'subnet', limit: 1 } });
assert.equal(JSON.parse(notes.content[0].text)[0].title, 'Subnet plans');
const agenda = await request('tools/call', { name: 'killendar_agenda', arguments: { date: '2026-09-28', days: 7, limit: 5 } });
assert.equal(JSON.parse(agenda.content[0].text)[0].title, 'Field visit');
const appointment = await request('tools/call', { name: 'killendar_create_appointment', arguments: { title: 'Field visit', start: '2026-09-28T09:00', end: '2026-09-28T10:00' } });
assert.equal(JSON.parse(appointment.content[0].text).title, 'Field visit');
const files = await request('tools/call', { name: 'killershell_search_files', arguments: { root: resolve('.'), name: '*.txt', limit: 2 } });
assert.equal(JSON.parse(files.content[0].text).results.length, 1);
const network = await request('tools/call', { name: 'killerscan_local_network', arguments: {} });
assert.equal(JSON.parse(network.content[0].text).localIp, '192.0.2.10');
const scan = await request('tools/call', { name: 'killerscan_scan_network', arguments: {} });
assert.equal(JSON.parse(scan.content[0].text)[0].ip, '192.0.2.10');
const preflight = await request('tools/call', { name: 'killerpdf_preflight', arguments: { path: pdfSourceOne, profile: 'print' } });
assert.equal(JSON.parse(preflight.content[0].text).valid, true);
const mergedPath = join(updateDirectory, 'merged.pdf');
const merged = await request('tools/call', { name: 'killerpdf_merge', arguments: { inputs: [pdfSourceOne, pdfSourceTwo], output: mergedPath } });
assert.equal(JSON.parse(merged.content[0].text).inputCount, 2);
assert.equal((await readFile(mergedPath, 'utf8')), 'fixture pdf');
child.stdin.end();
updateServer.close();
await rm(updateDirectory, { recursive: true, force: true });
console.log('Native KillerMCP stdio handshake, tool listing, and tool call passed.');
