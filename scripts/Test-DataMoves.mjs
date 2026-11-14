import { existsSync, lstatSync, readFileSync, readdirSync, unlinkSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { Host, makeRun, parseOptions, refused, seeded, short, succeeded, usage, writeReport } from './review/host.mjs';

const sourceTools = {
  lorem: 'generate_lorem_ipsum', directory: 'killershell_list_directory', agenda: 'killendar_agenda', network: 'killerscan_local_network',
  processes: 'killershell_list_processes', services: 'killershell_list_services', eventlog: 'killershell_read_event_log', pdfinfo: 'killerpdf_document_info',
  shots: 'killershot_recent', scripts: 'list_killer_scripts', drives: 'killershell_list_drives', textfile: 'killershell_read_text_file',
};
const sinkTools = {
  'pdf-text': ['killer_create_pdf', 'killerpdf_search_text'], 'pdf-html': ['killer_create_pdf', 'killerpdf_search_text'],
  file: ['write_text_file_local'], 'pdf-pipeline': ['killer_create_pdf', 'killerpdf_merge', 'killerpdf_number_pages', 'killerpdf_optimize', 'killerpdf_export', 'killerpdf_search_text', 'killershell_hash_file'],
  'pdf-to-images-to-pdf': ['killer_create_pdf', 'killerpdf_render_pages', 'killerpdf_images_to_pdf', 'killerpdf_ocr', 'killerpdf_search_text'],
  note: ['killernotes_create', 'killernotes_get'], 'dir-pdf': ['killershell_export_directory_pdf', 'killerpdf_document_info'],
  'agenda-pdf': ['killendar_export_agenda_pdf', 'killerpdf_document_info'],
};
const sizes = [0, 0, 20_000, 31_000, 100_000, 400_000, 999_000];

function payloadFor(body, size, marker, tail) {
  if (!body.length) body = 'empty source';
  const target = size || body.length;
  const repeated = body.repeat(Math.ceil(target / body.length)).slice(0, target);
  return `${marker} żółć 日本\n${repeated}\n${tail}`;
}
function noteContent(text) {
  try { const parsed = JSON.parse(text); return parsed.content ?? parsed.markdown ?? parsed.note?.content ?? null; }
  catch { return null; }
}

export async function runMoves(options) {
  if (options.source && !Object.hasOwn(sourceTools, options.source)) throw new Error('Unknown source.');
  if (options.sink && !Object.hasOwn(sinkTools, options.sink)) throw new Error('Unknown sink.');
  const run = makeRun(options, 'moves'), host = new Host(options.host, options.hostArgs, run.environment), random = seeded(options.seed);
  const pick = list => list[Math.floor(random() * list.length)];
  const steps = [], results = [];
  const summary = { seed: options.seed, requested: options.count, passed: 0, expectedRefusals: 0, skipped: 0, unavailable: 0, failed: 0, fatalErrors: 0, healthFailures: 0, stepErrors: 0, timeouts: 0, transportErrors: 0, rpcErrors: 0, toolErrors: 0 };
  let tools = new Map();
  async function step(chain, name, args) {
    const reply = await host.call(name, args, options.timeout);
    const outcome = reply.timeout ? 'timeout' : reply.transportError ? 'transport-error' : reply.rpcError ? 'rpc-error' : reply.isError ? 'tool-error' : 'ok';
    if (!succeeded(reply)) summary.stepErrors++;
    if (reply.timeout) summary.timeouts++;
    if (reply.transportError) summary.transportErrors++;
    if (reply.rpcError) summary.rpcErrors++;
    if (reply.isError) summary.toolErrors++;
    steps.push({ chain, tool: name, outcome, ms: Math.round(reply.ms ?? 0), text: short(reply.text || JSON.stringify(reply.rpcError ?? reply.transportError)) });
    return reply;
  }
  const assertStep = reply => { if (!succeeded(reply)) throw new Error(short(reply.text || JSON.stringify(reply.rpcError ?? reply.transportError) || 'Tool timed out.')); return reply; };
  async function source(chain, name) {
    const inputs = {
      lorem: { paragraphCount: 10 }, directory: { path: run.input, limit: 100 }, agenda: { date: options.date, days: 31, limit: 100 }, network: {},
      processes: { limit: 100 }, services: { limit: 100 }, eventlog: { log: 'System', limit: 100 }, pdfinfo: { path: path.join(run.input, 'multi.pdf') },
      shots: { limit: 20 }, scripts: {}, drives: {}, textfile: { path: path.join(run.input, 'note.txt') },
    };
    return assertStep(await step(chain, sourceTools[name], inputs[name])).text;
  }
  async function findMarkers(chain, pdf, marker, tail) {
    for (const query of [marker, tail]) {
      const found = assertStep(await step(chain, 'killerpdf_search_text', { path: pdf, query }));
      if (!found.text.includes(query) || /"(?:count|total)"\s*:\s*0\b|"matches"\s*:\s*\[\s*\]/.test(found.text)) throw new Error(`PDF lost marker ${query}.`);
    }
  }
  try {
    await host.init(); tools = new Map((await host.tools()).map(tool => [tool.name, tool]));
    for (let index = 1; index <= options.count; index++) {
      const chain = `s${options.seed}-c${index}`, sourceName = options.source ?? pick(Object.keys(sourceTools)), size = options.size ?? pick(sizes), sink = options.sink ?? pick(Object.keys(sinkTools));
      const marker = `MARK${options.seed}x${index}`, tail = `TAIL${options.seed}x${index}`, started = performance.now();
      const result = { chain, source: sourceName, size, sink, outcome: 'failed', why: '' };
      const createSchema = tools.get('killernotes_create')?.inputSchema;
      const noteLimit = Math.min(500_000, createSchema?.properties?.content?.maxLength ?? 500_000);
      const deliberateNoteRefusal = sink === 'note' && size > noteLimit;
      const required = [sourceTools[sourceName], ...(deliberateNoteRefusal ? ['killernotes_create'] : sinkTools[sink])];
      try {
        if (!options.labAuthorized && sink !== 'file' && !deliberateNoteRefusal) { result.outcome = 'skipped'; result.why = 'App writes require explicit disposable-lab authorization.'; continue; }
        if (!options.labAuthorized && ['agenda', 'shots', 'scripts'].includes(sourceName)) { result.outcome = 'skipped'; result.why = 'Personal or external source requires disposable-lab authorization.'; continue; }
        if (required.some(name => options.filter && !options.filter.test(name))) { result.outcome = 'skipped'; result.why = 'A chain tool is excluded by --tool.'; continue; }
        const missing = required.filter(name => !tools.has(name));
        if (missing.length) { result.outcome = 'unavailable'; result.why = 'Tools not advertised: ' + missing.join(', '); continue; }
        const body = await source(chain, sourceName), payload = payloadFor(body, size, marker, tail);
        result.characters = payload.length;
        const out = suffix => path.join(run.output, chain + suffix);
        if (sink === 'file') {
          if (!existsSync(options.downloads) || lstatSync(options.downloads).isSymbolicLink() || !lstatSync(options.downloads).isDirectory()) { result.outcome = 'unavailable'; result.why = 'Downloads directory is missing or linked; supply the actual Downloads path.'; continue; }
          const destination = path.join(options.downloads, `killermcp-driver-${run.token}-${chain}.txt`);
          if (existsSync(destination)) throw new Error('The unique Downloads target already exists; nothing was overwritten.');
          result.downloadFile = destination;
          try {
            assertStep(await step(chain, 'write_text_file_local', { path: destination, text: payload }));
            if (!existsSync(destination) || lstatSync(destination).isSymbolicLink() || !lstatSync(destination).isFile()) throw new Error('The writer did not produce an ordinary file at the requested target.');
            const expected = Buffer.from(payload, 'utf8'), actual = readFileSync(destination);
            if (!expected.equals(actual)) { writeFileSync(out('-different-bytes.bin'), actual, { flag: 'wx' }); throw new Error('Full file bytes differ, including the tail.'); }
            result.verification = 'Every UTF-8 byte compared directly; Base64 read cap does not limit this check.';
          } finally {
            // The exact fresh filename belongs to this run. No folder or unrelated path is removed.
            if (existsSync(destination) && !lstatSync(destination).isSymbolicLink() && lstatSync(destination).isFile()) unlinkSync(destination);
          }
        } else if (sink === 'note') {
          const made = await step(chain, 'killernotes_create', { title: `Disposable driver ${run.token} ${chain}`, content: payload });
          if (deliberateNoteRefusal) {
            if (!refused(made) || !(made.rpcError?.code === -32602 || made.isError && /Invalid arguments:/i.test(made.text))) throw new Error('Oversized Notes payload did not receive a schema refusal.');
            result.outcome = 'expectedRefusals'; result.why = `Payload exceeds ${noteLimit} character Notes limit.`; continue;
          }
          assertStep(made);
          let created; try { created = JSON.parse(made.text); } catch { throw new Error('Note creation returned no readable id.'); }
          const id = created.id ?? created.noteId ?? created.note?.id;
          if (!Number.isSafeInteger(id)) throw new Error('Note creation returned no numeric id.');
          result.createdNoteId = id;
          const got = assertStep(await step(chain, 'killernotes_get', { id, maxChars: 50000 }));
          const content = noteContent(got.text);
          if (typeof content !== 'string' || !content.includes(marker) || payload.length <= 50000 && !content.includes(tail)) throw new Error('The actual note content read back did not contain the expected marker.');
          result.verification = payload.length <= 50000 ? 'Head and tail verified through get.' : 'Actual head marker verified through get; its 50,000-character cap prevents full-content verification.';
        } else if (sink === 'pdf-text' || sink === 'pdf-html') {
          const content = sink === 'pdf-html' ? `<pre>${payload.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')}</pre>` : payload;
          assertStep(await step(chain, 'killer_create_pdf', { content, format: sink === 'pdf-html' ? 'html' : 'text', title: chain, output: out('.pdf') }));
          await findMarkers(chain, out('.pdf'), marker, tail);
        } else if (sink === 'pdf-pipeline') {
          assertStep(await step(chain, 'killer_create_pdf', { content: payload, format: 'text', title: chain, output: out('-a.pdf') }));
          assertStep(await step(chain, 'killerpdf_merge', { inputs: [out('-a.pdf'), path.join(run.input, 'multi.pdf'), path.join(run.input, 'uni.pdf')], output: out('-b.pdf') }));
          assertStep(await step(chain, 'killerpdf_number_pages', { path: out('-b.pdf'), output: out('-c.pdf') }));
          assertStep(await step(chain, 'killerpdf_optimize', { path: out('-c.pdf'), output: out('-d.pdf') }));
          assertStep(await step(chain, 'killerpdf_export', { path: out('-d.pdf'), output: out('-e.md') }));
          await findMarkers(chain, out('-d.pdf'), marker, tail);
          assertStep(await step(chain, 'killershell_hash_file', { path: out('-e.md') }));
        } else if (sink === 'pdf-to-images-to-pdf') {
          assertStep(await step(chain, 'killer_create_pdf', { content: payload.slice(0, 20000) + '\n' + tail, format: 'text', title: chain, output: out('-a.pdf') }));
          const folder = out('-images');
          assertStep(await step(chain, 'killerpdf_render_pages', { path: out('-a.pdf'), outputFolder: folder, dpi: pick([72, 150, 300]), format: 'png' }));
          if (existsSync(folder) && lstatSync(folder).isSymbolicLink()) throw new Error('Render produced a linked output folder.');
          const images = existsSync(folder) ? readdirSync(folder).filter(name => name.endsWith('.png')).sort().map(name => path.join(folder, name)) : [];
          if (!images.length || images.length > 40) throw new Error('Render produced no images or exceeded the 40-page chain budget.');
          if (images.some(image => lstatSync(image).isSymbolicLink() || !lstatSync(image).isFile())) throw new Error('Render output contains a linked or non-file image.');
          assertStep(await step(chain, 'killerpdf_images_to_pdf', { images, output: out('-b.pdf') }));
          assertStep(await step(chain, 'killerpdf_ocr', { path: out('-b.pdf'), output: out('-c.pdf') }));
          await findMarkers(chain, out('-c.pdf'), marker, tail);
        } else if (sink === 'dir-pdf' || sink === 'agenda-pdf') {
          const args = sink === 'dir-pdf' ? { path: run.input, limit: 100, output: out('.pdf') } : { date: options.date, days: 31, limit: 100, output: out('.pdf') };
          assertStep(await step(chain, sinkTools[sink][0], args));
          assertStep(await step(chain, 'killerpdf_document_info', { path: out('.pdf') }));
          result.verification = 'Export and readable PDF verified; this sink does not carry the generated payload markers.';
        }
        result.outcome = 'passed';
      } catch (error) { result.outcome = 'failed'; result.why = error.message; }
      finally {
        result.ms = Math.round(performance.now() - started);
        summary[result.outcome]++;
        results.push(result);
      }
    }
    const ping = await host.request('ping', undefined, 5000);
    summary.alive = !!ping.message?.result && !ping.message?.error;
    if (!summary.alive) summary.healthFailures++;
  } catch (error) { summary.fatal = error.message; summary.fatalErrors++; }
  finally {
    const closed = await host.close();
    summary.closeHung = closed.hung; summary.exitCode = closed.code;
    summary.fixturesChanged = run.preservation(); summary.protocolErrors = host.protocolErrors; summary.unmatchedReplies = host.unmatched;
    if (closed.hung || closed.code !== 0 || summary.fixturesChanged.length || host.protocolErrors) summary.healthFailures++;
    summary.steps = steps.length; summary.resultsDirectory = run.directory;
    writeReport(run, { summary, results, steps, hostStderr: host.stderr });
  }
  console.log(JSON.stringify(summary));
  return summary.failed || summary.fatalErrors || summary.healthFailures ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { const options = parseOptions(process.argv.slice(2)); process.exitCode = options.help ? (console.log(usage('Test-DataMoves.mjs')), 0) : await runMoves(options); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
