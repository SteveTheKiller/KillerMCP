import { execFile } from 'node:child_process';
import { constants, existsSync, statSync } from 'node:fs';
import { copyFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, extname, isAbsolute, join } from 'node:path';

const maxPdfBytes = 64 * 1024 * 1024;
const pathSchema = { type: 'string', minLength: 1, maxLength: 1024, description: 'Absolute path to a local PDF' };
const tools = [
  {
    name: 'killerpdf_preflight',
    description: 'Check a local PDF with KillerPDF preflight and return findings without changing the file.',
    command: '--preflight',
    inputSchema: {
      type: 'object',
      properties: { path: pathSchema, profile: { type: 'string', enum: ['general', 'attachments', 'print'], default: 'general' } },
      required: ['path'],
      additionalProperties: false,
    },
  },
  {
    name: 'killerpdf_accessibility',
    description: 'Check a local PDF for accessibility findings using KillerPDF without changing the file.',
    command: '--accessibility',
    inputSchema: { type: 'object', properties: { path: pathSchema }, required: ['path'], additionalProperties: false },
  },
];
const mergeTool = {
  name: 'killerpdf_merge',
  description: 'Merge two to eight local PDFs with KillerPDF into one new PDF. Use for requests such as "killer merge these PDFs". Never replaces an existing output file.',
  inputSchema: {
    type: 'object',
    properties: {
      inputs: { type: 'array', items: pathSchema, minItems: 2, maxItems: 8 },
      output: { ...pathSchema, description: 'Absolute path for a new merged PDF' },
    },
    required: ['inputs', 'output'],
    additionalProperties: false,
  },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function validate(input, command) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return 'Expected PDF arguments';
  if (typeof input.path !== 'string' || !isAbsolute(input.path) || input.path.length > 1024
    || extname(input.path).toLowerCase() !== '.pdf') return 'Path must name an absolute PDF file';
  if (Object.keys(input).some(key => !['path', ...(command === '--preflight' ? ['profile'] : [])].includes(key))) {
    return 'Unknown PDF argument';
  }
  if (command === '--preflight' && input.profile !== undefined
    && !['general', 'attachments', 'print'].includes(input.profile)) return 'Invalid preflight profile';
  try {
    const details = statSync(input.path);
    if (!details.isFile() || details.size > maxPdfBytes) return 'PDF must be a file no larger than 64 MiB';
  }
  catch { return 'Unable to read PDF file'; }
  return null;
}

function call(path, command, input) {
  const problem = validate(input, command);
  if (problem) return Promise.resolve(result(problem, true));
  const args = [command, input.path, '--json'];
  if (command === '--preflight') args.push('--profile', input.profile ?? 'general');
  return new Promise(resolve => {
    execFile(path, args, { encoding: 'utf8', windowsHide: true, timeout: 45000, maxBuffer: 262144 },
      (error, stdout, stderr) => {
        if (error && error.code !== 3) {
          resolve(result((stderr || stdout || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const parsed = JSON.parse(stdout);
          if (typeof parsed !== 'object' || parsed === null) throw new Error('Invalid report');
          resolve(result(JSON.stringify(parsed)));
        }
        catch {
          resolve(result('KillerPDF returned an invalid report', true));
        }
      });
  });
}

function validateMerge(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !['inputs', 'output'].includes(key))
    || !Array.isArray(input.inputs) || input.inputs.length < 2 || input.inputs.length > 8) {
    return 'Provide two to eight input PDFs and a new output path';
  }
  if (typeof input.output !== 'string' || !isAbsolute(input.output) || input.output.length > 1024
    || extname(input.output).toLowerCase() !== '.pdf') return 'Output must be an absolute PDF path';
  try {
    if (!statSync(dirname(input.output)).isDirectory()) return 'Output folder does not exist';
    if (existsSync(input.output)) return 'Output already exists';
  }
  catch { return 'Output folder does not exist'; }
  let totalBytes = 0;
  for (const path of input.inputs) {
    if (typeof path !== 'string' || !isAbsolute(path) || path.length > 1024
      || extname(path).toLowerCase() !== '.pdf') return 'Inputs must be absolute PDF paths';
    try {
      const details = statSync(path);
      if (!details.isFile() || details.size > maxPdfBytes) return 'Each input must be a PDF no larger than 64 MiB';
      totalBytes += details.size;
    }
    catch { return 'Unable to read an input PDF'; }
  }
  if (totalBytes > 128 * 1024 * 1024) return 'Combined inputs must be no larger than 128 MiB';
  return null;
}

async function merge(path, input) {
  const problem = validateMerge(input);
  if (problem) return result(problem, true);
  let temporary;
  try {
    temporary = await mkdtemp(join(tmpdir(), 'killermcp-pdf-'));
    const output = join(temporary, 'merged.pdf');
    const response = await new Promise(resolve => {
      execFile(path, ['--merge', output, ...input.inputs],
        { encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 8192 },
        (error, stdout, stderr) => resolve({ error, stdout, stderr }));
    });
    if (response.error) return result((response.stderr || response.stdout || response.error.message).trim().slice(0, 1024), true);
    if (!existsSync(output)) return result('KillerPDF did not create the merged PDF', true);
    await copyFile(output, input.output, constants.COPYFILE_EXCL);
    return result(JSON.stringify({ output: input.output, inputCount: input.inputs.length }));
  }
  catch (error) { return result(error.code === 'EEXIST' ? 'Output already exists' : error.message.slice(0, 1024), true); }
  finally { if (temporary) await rm(temporary, { recursive: true, force: true }); }
}

export async function createKillerPdfAdapters(path) {
  let available = false;
  try {
    available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile());
  }
  catch { /* An inaccessible app cannot be offered as a tool. */ }
  if (!available) return [];
  const help = await new Promise(resolve => {
    execFile(path, ['--help'], { encoding: 'utf8', windowsHide: true, timeout: 8000, maxBuffer: 65536 },
      (error, stdout) => resolve(error ? '' : stdout));
  });
  const adapters = [];
  if (/^\s*--merge <out\.pdf>/m.test(help)) {
    adapters.push({ tool: mergeTool, call: input => merge(path, input) });
  }
  for (const { command, ...tool } of tools) {
    if (help.includes(`${command} <in.pdf>`)) adapters.push({ tool, call: input => call(path, command, input) });
  }
  return adapters;
}
