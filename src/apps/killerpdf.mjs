import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { extname, isAbsolute } from 'node:path';

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

export function createKillerPdfAdapters(path) {
  let available = false;
  try {
    available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile());
  }
  catch { /* An inaccessible app cannot be offered as a tool. */ }
  if (!available) return [];
  return tools.map(({ command, ...tool }) => ({ tool, call: input => call(path, command, input) }));
}
