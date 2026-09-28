import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { isAbsolute, join } from 'node:path';
import { createInterface } from 'node:readline';

const directory = process.argv[2];
if (!directory || !isAbsolute(directory)) throw new Error('Pass an absolute native package directory');
const executable = join(directory, process.platform === 'win32' ? 'KillerMCP.exe' : 'KillerMCP');
const configurator = join(directory, process.platform === 'win32' ? 'KillerMCP.Configure.exe' : 'KillerMCP.Configure');
if (!existsSync(executable) || !existsSync(configurator) || !existsSync(join(directory, 'manifest.json'))) throw new Error('Native package files are missing');

const environment = { ...process.env };
environment.KILLERMCP_UPDATE_API = 'http://127.0.0.1:1/unavailable';
environment.KILLERMCP_UPDATE_CACHE = join(tmpdir(), `killermcp-native-package-${process.pid}.json`);
for (const variable of ['KILLERPDF_CLI', 'KILLERNOTES_CLI', 'KILLERSCAN_CLI', 'KILLERSHELL_CLI', 'KILLENDAR_CLI']) {
  environment[variable] = join(directory, 'missing-app-cli');
}

const child = spawn(executable, [], { cwd: tmpdir(), env: environment, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
let nextId = 1;
let stderr = '';
const pending = new Map();
child.stderr.setEncoding('utf8');
child.stderr.on('data', chunk => stderr += chunk);
createInterface({ input: child.stdout }).on('line', line => {
  const message = JSON.parse(line);
  const request = pending.get(message.id);
  if (request) { pending.delete(message.id); clearTimeout(request.timeout); request.resolve(message.result); }
});

function request(method, params = {}) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`Timed out waiting for ${method}: ${stderr}`));
    }, 20000);
    pending.set(id, { resolve, reject, timeout });
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

try {
  const initialized = await request('initialize', { protocolVersion: '2025-06-18' });
  assert.equal(initialized.serverInfo.name, 'KillerMCP');
  assert.equal(initialized.serverInfo.version, '0.3.1');
  const listed = await request('tools/list');
  assert.ok(listed.tools.some(tool => tool.name === 'killermcp_update_status'));
  assert.ok(listed.tools.some(tool => tool.name === 'convert_case'));
  const converted = await request('tools/call', { name: 'convert_case', arguments: { text: 'Hello World' } });
  assert.equal(JSON.parse(converted.content[0].text).find(item => item.label === 'Paramcase').value, 'hello-world');
  process.stdout.write('Native KillerMCP package passed MCP discovery and tool call.\n');
}
finally {
  child.stdin.end();
  child.kill();
}

const clientRoot = await mkdtemp(join(tmpdir(), 'killermcp-native-clients-'));
try {
  const clientEnvironment = { ...environment, PATH: '', CODEX_CLI_PATH: join(clientRoot, 'missing-codex') };
  for (const name of ['CLAUDE', 'CURSOR', 'COPILOT', 'GEMINI', 'WINDSURF', 'CLAUDE_DESKTOP']) {
    const path = join(clientRoot, name, 'settings.json');
    await mkdir(join(clientRoot, name), { recursive: true });
    await writeFile(path, '{"preservedSetting":"keep"}');
    clientEnvironment[`KILLERMCP_TEST_${name}_CONFIG`] = path;
  }
  const registered = spawnSync(configurator, ['register', executable], { env: clientEnvironment, encoding: 'utf8', windowsHide: true });
  assert.equal(registered.status, 0, registered.stderr);
  const cursor = JSON.parse(await readFile(clientEnvironment.KILLERMCP_TEST_CURSOR_CONFIG, 'utf8'));
  assert.equal(cursor.preservedSetting, 'keep');
  assert.equal(cursor.mcpServers.killermcp.command, executable);
  const removed = spawnSync(configurator, ['unregister', executable], { env: clientEnvironment, encoding: 'utf8', windowsHide: true });
  assert.equal(removed.status, 0, removed.stderr);
  assert.equal(JSON.parse(await readFile(clientEnvironment.KILLERMCP_TEST_CURSOR_CONFIG, 'utf8')).mcpServers.killermcp, undefined);
  process.stdout.write('Native KillerMCP package passed isolated client registration.\n');
}
finally {
  await rm(clientRoot, { recursive: true, force: true });
}
