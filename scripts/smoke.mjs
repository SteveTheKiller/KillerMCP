import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const cli = process.argv[2];
const searchRoot = process.argv[3];
const benchCli = process.argv[4];
const scanCli = process.argv[5];
const pdfCli = process.argv[6];
const testPdf = process.argv[7];
if (!cli || !isAbsolute(cli) || !existsSync(cli) || !searchRoot || !isAbsolute(searchRoot)) {
  throw new Error('Pass the absolute KillerShell CLI path and an absolute search test directory');
}
if (benchCli && (!isAbsolute(benchCli) || !existsSync(benchCli))) {
  throw new Error('KillerBench CLI path must name an absolute executable file');
}
if (scanCli && (!isAbsolute(scanCli) || !existsSync(scanCli))) {
  throw new Error('KillerScan CLI path must name an absolute executable file');
}
if (pdfCli && (!isAbsolute(pdfCli) || !existsSync(pdfCli) || !testPdf || !isAbsolute(testPdf) || !existsSync(testPdf))) {
  throw new Error('Pass absolute KillerPDF executable and test PDF paths');
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
  if (includeShell && benchCli) env.KILLERBENCH_CLI = benchCli;
  else delete env.KILLERBENCH_CLI;
  if (includeShell && scanCli) env.KILLERSCAN_CLI = scanCli;
  else delete env.KILLERSCAN_CLI;
  if (includeShell && pdfCli) env.KILLERPDF_CLI = pdfCli;
  else delete env.KILLERPDF_CLI;
  if (!includeShell) {
    for (const name of ['KILLERSHELL_CLI', 'KILLERBENCH_CLI', 'KILLERSCAN_CLI', 'KILLERPDF_CLI']) {
      env[name] = join(root, 'missing-app-cli.exe');
    }
  }
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
  assert.equal(names.size, 95 + (benchCli ? 2 : 0) + (scanCli ? 1 : 0) + (pdfCli ? 2 : 0));
  assert.ok(names.has('killershell_search_files'));
  if (pdfCli) {
    assert.ok(names.has('killerpdf_preflight'));
    assert.ok(names.has('killerpdf_accessibility'));
    const preflight = await request('tools/call', { name: 'killerpdf_preflight', arguments: { path: testPdf } });
    assert.ok(preflight.result && !preflight.result.isError, JSON.stringify(preflight));
    assert.ok(Array.isArray(JSON.parse(preflight.result.content[0].text).findings));
    const accessibility = await request('tools/call', { name: 'killerpdf_accessibility', arguments: { path: testPdf } });
    assert.ok(accessibility.result && !accessibility.result.isError, JSON.stringify(accessibility));
    assert.ok(Array.isArray(JSON.parse(accessibility.result.content[0].text).findings));
    const invalidPdf = await request('tools/call', { name: 'killerpdf_preflight', arguments: { path: searchRoot } });
    assert.equal(invalidPdf.result?.isError, true);
  }
  if (scanCli) {
    assert.ok(names.has('killerscan_local_network'));
    const network = await request('tools/call', { name: 'killerscan_local_network', arguments: {} });
    assert.ok(network.result && !network.result.isError, JSON.stringify(network));
    assert.match(JSON.parse(network.result.content[0].text).localIp, /^\d{1,3}(?:\.\d{1,3}){3}$/);
    const invalidNetwork = await request('tools/call', { name: 'killerscan_local_network', arguments: { scan: true } });
    assert.equal(invalidNetwork.result?.isError, true);
  }
  if (benchCli) {
    assert.ok(names.has('killerbench_device_code'));
    assert.ok(names.has('killerbench_win32_code'));
    const device = await request('tools/call', { name: 'killerbench_device_code', arguments: { code: '10' } });
    assert.equal(JSON.parse(device.result.content[0].text).symbol, 'CM_PROB_FAILED_START');
    const win32 = await request('tools/call', { name: 'killerbench_win32_code', arguments: { code: '5' } });
    assert.equal(JSON.parse(win32.result.content[0].text).message, 'Access is denied.');
    const invalidCode = await request('tools/call', { name: 'killerbench_device_code', arguments: { code: '-1' } });
    assert.equal(invalidCode.result?.isError, true);
  }
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
process.stdout.write(`KillerMCP smoke passed: 94 KillerTools operations, one KillerShell tool${benchCli ? ', two KillerBench tools' : ''}${scanCli ? ', one KillerScan tool' : ''}${pdfCli ? ', and two KillerPDF tools' : ''}\n`);
