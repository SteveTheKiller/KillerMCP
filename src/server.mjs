import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { createKillerShellAdapter } from './apps/killershell.mjs';

const directory = fileURLToPath(new URL('./', import.meta.url));
const killerToolsBundle = join(directory, 'killertools.mjs');
const configuredShellCli = process.env.KILLERSHELL_CLI;
const shellAdapter = createKillerShellAdapter(configuredShellCli);

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
  if (shellAdapter && message.method === 'tools/call' && message.params?.name === shellAdapter.tool.name && message.id !== undefined) {
    shellAdapter.call(message.params.arguments).then(result => respond(message.id, result));
    return;
  }
  if (shellAdapter && message.method === 'tools/list' && message.id !== undefined) {
    pendingLists.add(JSON.stringify(message.id));
  }
  child.stdin.write(`${line}\n`);
});
readLines(child.stdout, line => {
  if (shellAdapter) {
    try {
      const message = JSON.parse(line);
      const key = JSON.stringify(message.id);
      if (pendingLists.delete(key) && Array.isArray(message.result?.tools) && !message.result.nextCursor
        && !message.result.tools.some(tool => tool.name === shellAdapter.tool.name)) {
        message.result.tools.push(shellAdapter.tool);
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
