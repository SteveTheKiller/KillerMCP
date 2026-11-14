import { spawn, spawnSync } from 'node:child_process';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { createHash, randomUUID } from 'node:crypto';
import { hostname, homedir } from 'node:os';
import path from 'node:path';

export const short = (text, length = 200) => String(text ?? '').replace(/\s+/g, ' ').slice(0, length);
export function seeded(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6D2B79F5) >>> 0;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

export function parseOptions(args, count = 30) {
  const result = { hostArgs: [], seed: 1, count, timeout: 150000, date: new Date().toISOString().slice(0, 10), downloads: path.join(homedir(), 'Downloads') };
  for (let index = 0; index < args.length; index++) {
    const name = args[index];
    if (name === '--help') return { help: true };
    if (name === '--lab-writes') { result.labWrites = true; continue; }
    if (!['--host', '--host-arg', '--root', '--seed', '--count', '--tool', '--timeout', '--downloads', '--lab-machine', '--date', '--sink', '--source', '--size'].includes(name)) throw new Error(`Unknown option: ${name}`);
    if (index + 1 >= args.length) throw new Error(`Missing value for ${name}`);
    const value = args[++index];
    if (name === '--host-arg') result.hostArgs.push(value);
    else result[name.slice(2).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = value;
  }
  if (!result.host) throw new Error('--host is required.');
  if (!result.root || !path.isAbsolute(result.root)) throw new Error('--root must be an absolute owned scratch directory.');
  if (!path.isAbsolute(result.host) || !path.isAbsolute(result.downloads)) throw new Error('Host and Downloads paths must be absolute.');
  for (const name of ['seed', 'count', 'timeout']) {
    result[name] = Number(result[name]);
    if (!Number.isSafeInteger(result[name]) || result[name] < (name === 'seed' ? 0 : 1) || result[name] > (name === 'timeout' ? 600000 : name === 'seed' ? 0xffffffff : 100000)) throw new Error(`Invalid ${name}.`);
  }
  result.filter = result.tool ? new RegExp(result.tool) : null;
  if (result.size !== undefined) {
    result.size = Number(result.size);
    if (!Number.isSafeInteger(result.size) || result.size < 0 || result.size > 4_000_000) throw new Error('Invalid size.');
  }
  result.labAuthorized = result.labWrites === true && result.labMachine?.toLowerCase() === hostname().toLowerCase();
  if (result.labWrites && !result.labAuthorized) throw new Error('--lab-writes requires --lab-machine to match this disposable machine exactly.');
  return result;
}

export const usage = name => `Usage: node ${name} --host <absolute executable> [--host-arg <argument>] --root <owned scratch directory> [--seed N] [--count N] [--tool regex] [--timeout milliseconds]\nApp mutations additionally require --lab-writes --lab-machine <current disposable machine name>. Data moves accept --sink, --source, --downloads and --date. Results remain in a new owned run directory. No packages are required.`;

const sandboxWriters = new Set([
  'killer_create_pdf', 'killerpdf_merge', 'killerpdf_split', 'killerpdf_render_pages', 'killerpdf_images_to_pdf', 'killerpdf_number_pages',
  'killerpdf_optimize', 'killerpdf_export', 'killerpdf_ocr', 'killerpdf_flatten', 'killerpdf_crop', 'killerpdf_set_metadata',
  'killershell_export_directory_pdf', 'killendar_export_agenda_pdf', 'decode_file_base64_local', 'write_text_file_local',
]);
export const allowsMutation = (tool, labAuthorized) => labAuthorized && sandboxWriters.has(tool.name);
export const safeRead = tool => tool.annotations?.readOnlyHint === true
  && !/^(?:killernotes_|killendar_|killerbench_|killershot_|open_browser_|get_browser_|search_gifs$|lookup_domain_|lookup_cve$|list_killer_scripts$)/.test(tool.name)
  && !/^killerpdf_(?:print|scan|sign|convert_office|convert_book)$/.test(tool.name);

function pdfFixture(text, pages = 1) {
  const objects = ['<< /Type /Catalog /Pages 2 0 R >>', `<< /Type /Pages /Kids [${Array.from({ length: pages }, (_, index) => `${4 + index * 2} 0 R`).join(' ')}] /Count ${pages} >>`, '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'];
  for (let index = 0; index < pages; index++) {
    const stream = `BT /F1 12 Tf 50 700 Td (${text} page ${index + 1}) Tj ET`;
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents ${5 + index * 2} 0 R >>`, `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}\nendstream`);
  }
  let output = '%PDF-1.4\n';
  const offsets = [0];
  for (const [index, object] of objects.entries()) { offsets.push(Buffer.byteLength(output)); output += `${index + 1} 0 obj\n${object}\nendobj\n`; }
  const xref = Buffer.byteLength(output);
  output += `xref\n0 ${offsets.length}\n0000000000 65535 f \n${offsets.slice(1).map(offset => `${String(offset).padStart(10, '0')} 00000 n \n`).join('')}trailer\n<< /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return output;
}

export function makeRun(options, kind) {
  const root = path.resolve(options.root);
  if (root === path.parse(root).root || root === homedir() || root === path.dirname(options.host)) throw new Error('Choose a dedicated scratch root, not a drive, home or host directory.');
  if (existsSync(root) && lstatSync(root).isSymbolicLink()) throw new Error('The scratch root must not be a link.');
  const owner = path.join(root, '.killermcp-review-root');
  if (existsSync(owner) && lstatSync(owner).isSymbolicLink()) throw new Error('The ownership marker must not be a link.');
  if (existsSync(root) && !existsSync(owner) && readdirSync(root).length) throw new Error('Existing scratch root has no driver ownership marker. Choose a new directory.');
  mkdirSync(root, { recursive: true });
  if (!existsSync(owner)) writeFileSync(owner, 'KillerMCP maintained review drivers\n', { flag: 'wx' });
  if (readFileSync(owner, 'utf8') !== 'KillerMCP maintained review drivers\n') throw new Error('Scratch root ownership marker differs.');
  const directory = mkdtempSync(path.join(root, `${kind}-s${options.seed}-`));
  const input = path.join(directory, 'in'), output = path.join(directory, 'out');
  mkdirSync(input); mkdirSync(output);
  const put = (name, content) => writeFileSync(path.join(input, name), content, { flag: 'wx' });
  put('small.pdf', pdfFixture('Small fixture'));
  put('multi.pdf', pdfFixture('Multi fixture', 3));
  put('uni.pdf', pdfFixture('Portable fixture'));
  put('note.txt', 'plain text fixture\nUnicode: żółć 日本 🎸\n');
  put('data.csv', 'name,city\nAda,London\nGrace,New York\n');
  put('page.png', Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aJXcAAAAASUVORK5CYII=', 'base64'));
  const victim = path.join(directory, 'victim.pdf');
  writeFileSync(victim, pdfFixture('Do not overwrite this sentinel'), { flag: 'wx' });
  const files = [...readdirSync(input).map(name => path.join(input, name)), victim];
  const hash = file => createHash('sha256').update(readFileSync(file)).digest('hex');
  const before = new Map(files.map(file => [file, hash(file)]));
  const preservation = () => {
    if (!existsSync(directory) || !existsSync(input) || lstatSync(directory).isSymbolicLink() || lstatSync(input).isSymbolicLink()) return files;
    return files.filter(file => {
      try { return !existsSync(file) || lstatSync(file).isSymbolicLink() || !lstatSync(file).isFile() || hash(file) !== before.get(file); }
      catch { return true; }
    });
  };
  writeFileSync(path.join(directory, 'update-preferences.json'), '{"Behavior":"off"}\n', { flag: 'wx' });
  return { directory, input, output, victim, token: randomUUID(), preservation,
    environment: { KILLERMCP_UPDATE_CACHE: path.join(directory, 'update-status.json'), KILLERMCP_TOOL_CALL_LOG: path.join(directory, 'tool-calls.jsonl') } };
}

export class Host {
  constructor(executable, args = [], environment = {}) {
    this.pending = new Map(); this.nextId = 1; this.stderr = ''; this.protocolErrors = 0; this.unmatched = 0;
    this.child = spawn(executable, args, { env: { ...process.env, ...environment }, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    this.exited = new Promise(done => {
      const finish = (code, signal, error) => {
        if (this.finished) return;
        this.finished = true; this.exitCode = code; this.spawnError = error?.message;
        for (const waiter of this.pending.values()) waiter({ transportError: error?.message ?? `Host exited (${code ?? signal}).` });
        this.pending.clear(); done({ code, signal });
      };
      this.child.once('exit', (code, signal) => finish(code, signal));
      this.child.once('error', error => finish(null, null, error));
    });
    let buffer = '';
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', chunk => {
      buffer += chunk;
      if (buffer.length > 32 * 1024 * 1024) { this.protocolErrors++; this.child.kill(); return; }
      let index;
      while ((index = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, index).replace(/\r$/, ''); buffer = buffer.slice(index + 1);
        let message;
        try { message = JSON.parse(line); } catch { this.protocolErrors++; continue; }
        const key = JSON.stringify(message.id), waiter = this.pending.get(key);
        if (waiter) { this.pending.delete(key); waiter({ message, bytes: Buffer.byteLength(line) }); }
        else this.unmatched++;
      }
    });
    this.child.stderr.setEncoding('utf8');
    this.child.stderr.on('data', chunk => { this.stderr = (this.stderr + chunk).slice(-65536); });
    this.child.stdin.on('error', () => {});
  }
  request(method, params, timeout = 60000) {
    if (this.finished) return Promise.resolve({ transportError: this.spawnError ?? 'Host has exited.' });
    const id = this.nextId++, start = performance.now();
    return new Promise(done => {
      const key = JSON.stringify(id);
      const timer = setTimeout(() => {
        this.pending.delete(key);
        if (method === 'tools/call' && !this.finished) this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId: id } }) + '\n');
        done({ timeout: true, ms: performance.now() - start });
      }, timeout);
      this.pending.set(key, reply => { clearTimeout(timer); done({ ...reply, ms: performance.now() - start }); });
      this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params === undefined ? {} : { params }) }) + '\n');
    });
  }
  async init() {
    const reply = await this.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'maintained-review-driver', version: '1' } });
    if (!reply.message?.result) throw new Error(`Initialize failed: ${short(JSON.stringify(reply))}`);
    this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
    return reply;
  }
  async tools() {
    const reply = await this.request('tools/list', undefined, 90000);
    if (!Array.isArray(reply.message?.result?.tools)) throw new Error(`tools/list failed: ${short(JSON.stringify(reply))}`);
    return reply.message.result.tools;
  }
  async call(name, arguments_, timeout = 150000) {
    const reply = await this.request('tools/call', { name, arguments: arguments_ }, timeout);
    const result = reply.message?.result;
    return { name, ms: reply.ms, timeout: !!reply.timeout, transportError: reply.transportError,
      rpcError: reply.message?.error, isError: !!result?.isError,
      text: (result?.content ?? []).filter(item => item.type === 'text').map(item => item.text).join('\n') };
  }
  async close(timeout = 15000) {
    if (!this.finished) this.child.stdin.end();
    let timer;
    const exit = await Promise.race([this.exited, new Promise(done => { timer = setTimeout(() => done(null), timeout); })]);
    clearTimeout(timer);
    if (exit) return { hung: false, code: exit.code };
    if (this.child.pid && process.platform === 'win32') {
      const taskkill = path.join(process.env.SystemRoot ?? 'C:\\Windows', 'System32', 'taskkill.exe');
      spawnSync(taskkill, ['/PID', String(this.child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore', timeout: 5000 });
    } else this.child.kill('SIGKILL');
    let killTimer;
    await Promise.race([this.exited, new Promise(done => { killTimer = setTimeout(done, 5000); })]);
    clearTimeout(killTimer);
    return { hung: true, code: this.exitCode };
  }
}

export const succeeded = reply => !reply.timeout && !reply.transportError && !reply.rpcError && !reply.isError;
export const refused = reply => !reply.timeout && !reply.transportError && (!!reply.rpcError || reply.isError);
export function writeReport(run, report) { writeFileSync(path.join(run.directory, 'results.json'), JSON.stringify(report, null, 2), { flag: 'wx' }); }
