import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { createKillerBenchAdapters } from './apps/killerbench.mjs';
import { createKillerScanAdapter } from './apps/killerscan.mjs';
import { createKillerShellAdapter } from './apps/killershell.mjs';

const directory = fileURLToPath(new URL('./', import.meta.url));
const killerToolsBundle = join(directory, 'killertools.mjs');
const configuredShellCli = process.env.KILLERSHELL_CLI;
const shellAdapter = createKillerShellAdapter(configuredShellCli);
const configuredBenchCli = process.env.KILLERBENCH_CLI;
const benchAdapters = createKillerBenchAdapters(configuredBenchCli);
const configuredScanCli = process.env.KILLERSCAN_CLI;
const scanAdapter = createKillerScanAdapter(configuredScanCli);
const adapters = [shellAdapter, scanAdapter, ...benchAdapters].filter(Boolean);
const adaptersByName = new Map(adapters.map(adapter => [adapter.tool.name, adapter]));

function respond(id, result) {
  process.stdout.write(`${JSON.stringify({ jsonrpc: '2.0', id, result })}\n`);
}

function readLines(stream, onLine) {
  let buffer = '';
  stream.setEncoding('utf8');
  stream.on('data', chunk => {
    buffer += chunk;
    if (buffer.length > 8_000_000) {
      process.stderr.write('MCP message exceeded 8 MB\n');
      process.exit(1);
    }
    let newline;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).trimEnd();
      buffer = buffer.slice(newline + 1);
      if (line) onLine(line);
    }
  });
}

if (!existsSync(killerToolsBundle)) {
  process.stderr.write('The KillerTools MCP bundle is missing. Build KillerMCP first.\n');
  process.exit(1);
}
if (configuredShellCli && !shellAdapter) {
  process.stderr.write('KILLERSHELL_CLI does not point to an absolute executable file. KillerShell tools are unavailable.\n');
}
if (configuredBenchCli && benchAdapters.length === 0) {
  process.stderr.write('KILLERBENCH_CLI does not point to an absolute executable file. KillerBench tools are unavailable.\n');
}
if (configuredScanCli && !scanAdapter) {
  process.stderr.write('KILLERSCAN_CLI does not point to an absolute executable file. KillerScan tools are unavailable.\n');
}

const child = spawn(process.execPath, [killerToolsBundle], {
  stdio: ['pipe', 'pipe', 'inherit'],
  windowsHide: true,
});
const pendingLists = new Set();
readLines(process.stdin, line => {
  let message;
  try { message = JSON.parse(line); }
  catch {
    child.stdin.write(`${line}\n`);
    return;
  }
  const adapter = adaptersByName.get(message.params?.name);
  if (adapter && message.method === 'tools/call' && message.id !== undefined) {
    adapter.call(message.params.arguments).then(result => respond(message.id, result));
    return;
  }
  if (adapters.length && message.method === 'tools/list' && message.id !== undefined) {
    pendingLists.add(JSON.stringify(message.id));
  }
  child.stdin.write(`${line}\n`);
});
readLines(child.stdout, line => {
  if (adapters.length) {
    try {
      const message = JSON.parse(line);
      const key = JSON.stringify(message.id);
      if (pendingLists.delete(key) && Array.isArray(message.result?.tools) && !message.result.nextCursor) {
        const existing = new Set(message.result.tools.map(tool => tool.name));
        for (const adapter of adapters) {
          if (!existing.has(adapter.tool.name)) message.result.tools.push(adapter.tool);
        }
        process.stdout.write(`${JSON.stringify(message)}\n`);
        return;
      }
    }
    catch { /* Forward the original line so the client can report a protocol error. */ }
  }
  process.stdout.write(`${line}\n`);
});
process.stdin.on('end', () => child.stdin.end());
child.on('error', error => {
  process.stderr.write(`KillerTools MCP failed to start: ${error.message}\n`);
  process.exitCode = 1;
});
child.on('exit', code => {
  if (code !== 0) process.exitCode = code || 1;
  process.stdin.pause();
});
