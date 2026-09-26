import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { isAbsolute, join } from 'node:path';
import { createInterface } from 'node:readline';

const directory = process.argv[2];
if (!directory || !isAbsolute(directory)) throw new Error('Pass an absolute isolated install directory');
const node = join(directory, 'node.exe');
const server = join(directory, 'killermcp.mjs');
if (!existsSync(node) || !existsSync(server)) throw new Error('Installed runtime files are missing');

const environment = { ...process.env };
for (const variable of [
  'KILLERPDF_CLI', 'KILLERNOTES_CLI', 'KILLERSCAN_CLI',
  'KILLERSHELL_CLI', 'KILLENDAR_CLI', 'KILLERBENCH_CLI',
]) environment[variable] = join(directory, 'missing-app-cli.exe');

const child = spawn(node, [server], {
  cwd: tmpdir(), env: environment, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true,
});
let nextId = 1;
let stderr = '';
const pending = new Map();
child.stderr.setEncoding('utf8');
child.stderr.on('data', chunk => stderr += chunk);
createInterface({ input: child.stdout }).on('line', line => {
  const message = JSON.parse(line);
  const request = pending.get(message.id);
  if (request) { pending.delete(message.id); clearTimeout(request.timeout); request.resolve(message); }
});
child.on('error', error => {
  for (const request of pending.values()) { clearTimeout(request.timeout); request.reject(error); }
  pending.clear();
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
  const initialized = await request('initialize', {
    protocolVersion: '2025-06-18', capabilities: {},
    clientInfo: { name: 'killermcp-installer-smoke', version: '0.1.0' },
  });
  assert.ok(initialized.result, JSON.stringify(initialized));
  child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);
  const listed = await request('tools/list');
  assert.equal(listed.result?.tools?.length, 94);
  const converted = await request('tools/call', {
    name: 'convert_case', arguments: { text: 'Hello World' },
  });
  assert.ok(converted.result && !converted.result.isError, JSON.stringify(converted));
  assert.ok(converted.result?.content?.length);
  process.stdout.write('Installed KillerMCP runtime passed MCP discovery and tool call\n');
}
finally {
  child.stdin.end();
  child.kill();
}
