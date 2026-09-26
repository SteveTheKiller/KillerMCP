import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const cli = process.argv[2];
const searchRoot = process.argv[3];
if (!cli || !isAbsolute(cli) || !existsSync(cli) || !searchRoot || !isAbsolute(searchRoot)) {
  throw new Error('Pass the absolute KillerShell CLI path and an absolute search test directory');
}

const root = fileURLToPath(new URL('../', import.meta.url));
const server = process.env.KILLERMCP_SERVER ?? join(root, 'dist', 'killermcp.mjs');
if (!isAbsolute(server) || !existsSync(server)) throw new Error('KillerMCP server is missing');
const node = process.env.KILLERMCP_NODE ?? process.execPath;
if (!isAbsolute(node) || !existsSync(node)) throw new Error('KillerMCP Node executable is missing');

async function withServer(includeShell, verify) {
  const env = { ...process.env };
  if (includeShell) env.KILLERSHELL_CLI = cli;
  else delete env.KILLERSHELL_CLI;
  const child = spawn(node, [server], { cwd: root, env, stdio: ['pipe', 'pipe', 'pipe'] });
  const pending = new Map();
  let buffer = '';
  let stderr = '';
  let nextId = 1;
  child.stderr.on('data', chunk => stderr += chunk);
  child.stdout.on('data', chunk => {
    buffer += chunk;
    let newline;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + 1);
      if (!line.trim()) continue;
      const message = JSON.parse(line);
      const resolve = pending.get(message.id);
      if (resolve) {
        pending.delete(message.id);
        resolve(message);
      }
    }
  });
  function request(method, params = {}) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}: ${stderr}`));
      }, 45000);
      pending.set(id, message => {
        clearTimeout(timeout);
        resolve(message);
      });
      child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
    });
  }
  try {
    const initialized = await request('initialize', {
      protocolVersion: '2025-06-18',
      capabilities: {},
      clientInfo: { name: 'killermcp-smoke', version: '0.1.0' },
    });
    assert.ok(initialized.result, JSON.stringify(initialized));
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);
    await verify(request);
  }
  finally {
    child.stdin.end();
    child.kill();
  }
}

await withServer(true, async request => {
  const listed = await request('tools/list');
  assert.ok(listed.result?.tools, JSON.stringify(listed));
  const names = new Set(listed.result.tools.map(tool => tool.name));
  assert.equal(names.size, 95);
  assert.ok(names.has('killershell_search_files'));
  assert.ok(names.has('convert_case'));
  const converted = await request('tools/call', { name: 'convert_case', arguments: { text: 'Hello World' } });
  assert.ok(converted.result && !converted.result.isError, JSON.stringify(converted));
  const searched = await request('tools/call', {
    name: 'killershell_search_files',
    arguments: { root: searchRoot, name: 'SearchEngine.cs', content: 'ResultsBatch', limit: 5 },
  });
  assert.ok(searched.result && !searched.result.isError, JSON.stringify(searched));
  const output = JSON.parse(searched.result.content[0].text);
  assert.ok(output.results.some(item => item.path.endsWith('SearchEngine.cs')));
  const invalid = await request('tools/call', { name: 'killershell_search_files', arguments: { root: searchRoot } });
  assert.equal(invalid.result?.isError, true);
});
await withServer(false, async request => {
  const listed = await request('tools/list');
  assert.equal(listed.result.tools.length, 94);
  assert.ok(!listed.result.tools.some(tool => tool.name === 'killershell_search_files'));
});
process.stdout.write('KillerMCP smoke passed: 94 KillerTools operations and one conditional KillerShell tool\n');
