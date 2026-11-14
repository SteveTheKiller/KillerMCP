import { existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { Host, allowsMutation, makeRun, parseOptions, refused, safeRead, seeded, short, usage, writeReport } from './review/host.mjs';

const inputNames = new Set(['path', 'changed', 'data', 'macroFile', 'bookmarks', 'pfx', 'root', 'folder', 'inputs', 'images', 'files', 'recipients']);
const outputNames = new Set(['output', 'outputFolder', 'reviewFolder']);
const strings = ['alpha', 'Hello World', '--output', '-h', '/?', '"quoted" & calc | more > x %PATH% $(whoami) `id`', 'line1\nline2\r\nline3', 'Zażółć 日本語 🎸', '', ' ', '../../missing', '0', 'null', '{"a":1}', '<script>alert(1)</script>', 'a'.repeat(300)];

export function buildCases(tool, options, run, random, tracked) {
  const pick = list => list[Math.floor(random() * list.length)];
  const schema = tool.inputSchema ?? {}, properties = schema.properties ?? {}, names = Object.keys(properties);
  let serial = 0;
  const nextOutput = folder => path.join(run.output, `${tool.name}-${++serial}${folder ? '' : '.pdf'}`);
  const fixture = name => path.join(run.input, name);
  function input(name) {
    if (tool.name === 'killershell_read_registry_key') return `HKCU\\Software\\KillerMCP.Driver\\missing-${run.token}`;
    if (name === 'root' || name === 'folder' || /list_directory|search_files|storage_report|export_directory/.test(tool.name)) return run.input;
    if (name === 'data') return fixture('data.csv');
    if (/read_text_file|encode_file/.test(tool.name) || name === 'bookmarks' || name === 'macroFile') return fixture('note.txt');
    if (/images_to_pdf|import_image|read_qr|read_text|edit_image/.test(tool.name) || name === 'images') return fixture('page.png');
    return fixture(pick(['small.pdf', 'multi.pdf', 'uni.pdf']));
  }
  function value(name, definition, depth = 0) {
    if (definition.enum?.length) return pick(definition.enum);
    const type = Array.isArray(definition.type) ? definition.type.find(item => item !== 'null') : definition.type;
    if (type === 'boolean') return random() < 0.5;
    if (type === 'integer' || type === 'number') {
      const minimum = definition.minimum ?? 0, maximum = Math.min(definition.maximum ?? 100, minimum + 50);
      const result = random() < 0.3 ? pick([minimum, maximum]) : minimum + random() * (maximum - minimum);
      return type === 'integer' ? Math.round(result) : Math.round(result * 100) / 100;
    }
    if (type === 'array') {
      const count = Math.min(definition.maxItems ?? 3, Math.max(definition.minItems ?? 1, 1 + Math.floor(random() * 3)));
      return Array.from({ length: count }, () => value(name, definition.items ?? { type: 'string' }, depth + 1));
    }
    if (type === 'object') return depth > 4 ? {} : Object.fromEntries((definition.required ?? []).map(key => [key, value(key, definition.properties?.[key] ?? {}, depth + 1)]));
    if (outputNames.has(name) || ['decode_file_base64_local', 'write_text_file_local'].includes(tool.name) && name === 'path') return nextOutput(name.endsWith('Folder'));
    if (inputNames.has(name)) return input(name);
    if (name === 'base64') return 'QUJD';
    if (name === 'pages' || name === 'wholePages') return pick(['1', '1-2', '2,1', '1-999', '0', '5-3', 'all', '1,,2', '-1']);
    if (name === 'date') return options.date;
    if (name === 'target' || name === 'host') return '127.0.0.1';
    if (name === 'mac' || name === 'macAddress') return pick(['00:1A:2B:3C:4D:5E', '001A2B3C4D5E', 'zz:zz', 'FF-FF-FF-FF-FF-FF']);
    let text = pick(strings), maximum = Math.min(definition.maxLength ?? 10000, 32768), minimum = Math.min(definition.minLength ?? 0, maximum);
    if (random() < 0.08) text = 'A'.repeat(maximum);
    return text.padEnd(minimum, 'x').slice(0, maximum);
  }
  const shaped = () => Object.fromEntries(names.filter(name => schema.required?.includes(name) || random() < 0.45).map(name => [name, value(name, properties[name])]));
  const cases = [];
  if (safeRead(tool) || allowsMutation(tool, options.labAuthorized)) for (let index = 0; index < 3; index++) cases.push({ kind: 'schema-shaped', args: shaped() });
  if (schema.additionalProperties === false) cases.push({ kind: 'must-refuse:extra-property', mustRefuse: true, args: { ...shaped(), [`__driver_unadvertised_${run.token}`]: true } });
  const typed = names.find(name => properties[name].type || properties[name].enum);
  if (typed) {
    const definition = properties[typed], types = [].concat(definition.type ?? []);
    const wrong = [null, {}, [], false, 12345, 'invalid', { [`driver-${run.token}`]: true }].find(candidate => {
      const type = candidate === null ? 'null' : Array.isArray(candidate) ? 'array' : typeof candidate === 'number' && Number.isInteger(candidate) ? 'integer' : typeof candidate;
      return definition.enum ? !definition.enum.some(item => JSON.stringify(item) === JSON.stringify(candidate)) : !types.includes(type) && !(type === 'integer' && types.includes('number'));
    });
    if (wrong !== undefined) cases.push({ kind: 'must-refuse:wrong-type', mustRefuse: true, args: { ...shaped(), [typed]: wrong } });
  }
  if (schema.required?.length) { const args = shaped(); delete args[schema.required[0]]; cases.push({ kind: 'must-refuse:missing-required', mustRefuse: true, args }); }
  const bounded = names.find(name => ['integer', 'number'].includes(properties[name].type) && Number.isFinite(properties[name].maximum + 1) && properties[name].maximum + 1 > properties[name].maximum);
  if (bounded) cases.push({ kind: 'must-refuse:over-max', mustRefuse: true, args: { ...shaped(), [bounded]: properties[bounded].maximum + 1 } });
  const limited = names.find(name => properties[name].type === 'string' && properties[name].maxLength > 0 && properties[name].maxLength <= 1_000_000);
  if (limited) cases.push({ kind: 'must-refuse:over-length', mustRefuse: true, args: { ...shaped(), [limited]: 'B'.repeat(properties[limited].maxLength + 1) } });
  if (!(safeRead(tool) || allowsMutation(tool, options.labAuthorized))) return cases;
  const incoming = tool.name === 'killershell_read_registry_key' || ['decode_file_base64_local', 'write_text_file_local'].includes(tool.name) ? null : names.find(name => inputNames.has(name));
  if (incoming) {
    const missing = fixture(`missing-${run.token}.pdf`);
    const probes = [['relative', `..\\missing-${run.token}.pdf`, true], ['missing', missing, false], ['directory', run.input, false],
      ['unc-public', `\\\\192.0.2.1\\share\\missing-${run.token}.pdf`, true], ['unc-long', `\\\\?\\UNC\\192.0.2.1\\share\\missing-${run.token}.pdf`, true],
      ['device', '\\\\.\\NUL', true], ['ads', fixture('small.pdf') + ':driver-' + run.token, true], ['long', fixture('x'.repeat(900) + '.pdf'), false],
      ['fwdslash-unc', `//192.0.2.1/share/missing-${run.token}.pdf`, true], ['trailing-dot', fixture('small.pdf') + '.', false], ['file-url', pathToFileURL(missing).href, true]];
    for (const [kind, target, mustRefuse] of probes) cases.push({ kind: `path-in:${kind}`, mustRefuse, args: { ...shaped(), [incoming]: properties[incoming].type === 'array' ? [target] : target } });
  }
  const outgoing = names.find(name => outputNames.has(name)) ?? (['decode_file_base64_local', 'write_text_file_local'].includes(tool.name) ? 'path' : null);
  if (outgoing && options.labAuthorized) {
    const local = process.env.LOCALAPPDATA, roaming = process.env.APPDATA;
    const protectedRoots = [local && path.join(local, 'KillerMCP'), local && path.join(local, 'Programs'), roaming && path.join(roaming, 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup')].filter(Boolean);
    const targets = [run.victim, `\\\\192.0.2.1\\share\\driver-${run.token}.pdf`, ...protectedRoots.map(root => path.join(root, `driver-${run.token}${outgoing.endsWith('Folder') ? '' : '.pdf'}`))];
    for (const target of targets) {
      const remote = target.startsWith('\\\\') || target.startsWith('//');
      if (!remote && target !== run.victim) tracked.add(target);
      if (!remote && target !== run.victim && existsSync(target)) { cases.push({ kind: 'skip:preexisting-probe-target', skip: true }); continue; }
      cases.push({ kind: 'path-out:must-refuse', mustRefuse: true, args: { ...shaped(), [outgoing]: target } });
    }
  }
  return cases;
}

export async function runFuzz(options) {
  const run = makeRun(options, 'fuzz'), host = new Host(options.host, options.hostArgs, run.environment), random = seeded(options.seed), tracked = new Set();
  const results = [], skippedTools = [];
  const summary = { seed: options.seed, calls: 0, toolsListed: 0, toolsSelected: 0, toolsVisited: 0, schemaOnlyTools: 0, skipped: 0, unavailable: 0, toolErrors: 0, rpcErrors: 0, timeouts: 0, transportErrors: 0, mustRefuse: 0, unexpectedFailures: 0 };
  try {
    await host.init();
    const tools = await host.tools();
    summary.toolsListed = tools.length;
    const selected = tools.filter(tool => !options.filter || options.filter.test(tool.name));
    summary.toolsSelected = selected.length;
    if (!selected.length) throw new Error('No tools matched the selected filter.');
    for (const tool of selected) {
      summary.toolsVisited++;
      if (!(safeRead(tool) || allowsMutation(tool, options.labAuthorized))) {
        summary.schemaOnlyTools++; summary.skipped += 3;
        skippedTools.push({ tool: tool.name, reason: 'Three schema-shaped calls suppressed; only guaranteed schema refusals are eligible.' });
      }
      const cases = buildCases(tool, options, run, random, tracked);
      if (!cases.length) { summary.skipped++; skippedTools.push({ tool: tool.name, reason: 'No guaranteed schema refusal; mutation or external effect was not authorized.' }); }
      for (const [caseIndex, item] of cases.entries()) {
        if (summary.calls >= options.count) { summary.budgetLimited = true; summary.skipped += cases.length - caseIndex; break; }
        if (item.skip) { summary.skipped++; continue; }
        const reply = await host.call(tool.name, item.args, options.timeout);
        const outcome = reply.timeout ? 'timeout' : reply.transportError ? 'transport-error' : reply.rpcError ? 'rpc-error' : reply.isError ? 'tool-error' : 'ok';
        summary.calls++;
        if (reply.timeout) summary.timeouts++;
        if (reply.transportError) summary.transportErrors++;
        if (reply.rpcError) summary.rpcErrors++;
        if (reply.isError) summary.toolErrors++;
        if (item.mustRefuse) summary.mustRefuse++;
        const schemaRefusal = reply.rpcError?.code === -32602 || reply.isError && /Invalid arguments:/i.test(reply.text);
        const unexpected = reply.timeout || reply.transportError || item.mustRefuse && !(item.kind.startsWith('must-refuse:') ? schemaRefusal : refused(reply));
        if (unexpected) summary.unexpectedFailures++;
        results.push({ tool: tool.name, kind: item.kind, mustRefuse: !!item.mustRefuse, outcome, unexpected: !!unexpected, ms: Math.round(reply.ms ?? 0), text: short(reply.text || JSON.stringify(reply.rpcError ?? reply.transportError)), argumentKeys: Object.keys(item.args) });
      }
      if (summary.calls >= options.count) break;
    }
    const ping = await host.request('ping', undefined, 5000);
    summary.alive = !!ping.message?.result && !ping.message?.error;
    if (!summary.alive) summary.unexpectedFailures++;
  } catch (error) { summary.fatal = error.message; summary.unexpectedFailures++; }
  finally {
    const closed = await host.close();
    summary.closeHung = closed.hung; summary.exitCode = closed.code;
    summary.fixturesChanged = run.preservation();
    summary.victimChanged = summary.fixturesChanged.includes(run.victim);
    summary.stray = [...tracked].filter(target => existsSync(target));
    summary.protocolErrors = host.protocolErrors; summary.unmatchedReplies = host.unmatched;
    summary.unvisitedTools = summary.toolsSelected - summary.toolsVisited;
    if (closed.hung || closed.code !== 0 || summary.fixturesChanged.length || summary.stray.length || host.protocolErrors) summary.unexpectedFailures++;
    summary.errors = summary.toolErrors + summary.rpcErrors + summary.timeouts + summary.transportErrors;
    summary.resultsDirectory = run.directory;
    writeReport(run, { summary, skippedTools, results, hostStderr: host.stderr });
  }
  console.log(JSON.stringify(summary));
  return summary.unexpectedFailures ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { const options = parseOptions(process.argv.slice(2), 10000); process.exitCode = options.help ? (console.log(usage('Test-Fuzz.mjs')), 0) : await runFuzz(options); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
