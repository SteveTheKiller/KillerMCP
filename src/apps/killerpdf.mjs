import { execFile } from 'node:child_process';
import { constants, existsSync, statSync } from 'node:fs';
import { copyFile, mkdtemp, readdir, rename, rm } from 'node:fs/promises';
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

const pageRangeSchema = {
  type: 'string', minLength: 1, maxLength: 256, pattern: '^\\d+(?:-\\d+)?(?:,\\d+(?:-\\d+)?)*$',
  description: 'One-based pages such as 1-3,5,9-12',
};
const passwordSchema = { type: 'string', maxLength: 1024, description: 'PDF password when required' };
const newPdfSchema = { ...pathSchema, description: 'Absolute path for a new PDF that does not already exist' };
const newFolderSchema = { type: 'string', minLength: 1, maxLength: 1024, description: 'Absolute path for a new output folder' };

const operationTools = [
  {
    name: 'killerpdf_extract_pages', marker: '--extract-pages <in.pdf>', kind: 'file',
    description: 'Extract selected pages from a local PDF into a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, pages: pageRangeSchema, output: newPdfSchema }, required: ['path', 'pages', 'output'], additionalProperties: false },
    args: (input, output) => ['--extract-pages', input.path, input.pages, output],
  },
  {
    name: 'killerpdf_split', marker: '--split <in.pdf>', kind: 'directory',
    description: 'Split a local PDF into one new PDF per page in a new folder without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, outputFolder: newFolderSchema }, required: ['path', 'outputFolder'], additionalProperties: false },
    args: (input, output) => ['--split', input.path, output],
  },
  {
    name: 'killerpdf_decrypt', marker: '--decrypt <in.pdf>', kind: 'file',
    description: 'Remove PDF encryption into a new local PDF. Supply a password when the document requires one. Never changes the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, output: newPdfSchema, password: passwordSchema }, required: ['path', 'output'], additionalProperties: false },
    args: (input, output) => ['--decrypt', input.path, output, ...(input.password ? ['--password', input.password] : [])],
  },
  {
    name: 'killerpdf_render_pages', marker: '--to-image <in.pdf>', kind: 'directory',
    description: 'Render selected PDF pages into PNG or JPEG images in a new folder. Never changes the source.',
    inputSchema: {
      type: 'object',
      properties: {
        path: pathSchema, outputFolder: newFolderSchema, pages: pageRangeSchema,
        dpi: { type: 'integer', minimum: 36, maximum: 1200, default: 150 },
        format: { type: 'string', enum: ['png', 'jpg'], default: 'png' },
        transparent: { type: 'boolean', default: false }, password: passwordSchema,
      },
      required: ['path', 'outputFolder'], additionalProperties: false,
    },
    args: (input, output) => ['--to-image', input.path, output, '--dpi', String(input.dpi ?? 150), '--format', input.format ?? 'png',
      ...(input.pages ? ['--pages', input.pages] : []), ...(input.transparent ? ['--transparent'] : []),
      ...(input.password ? ['--password', input.password] : [])],
  },
  {
    name: 'killerpdf_flatten', marker: '--flatten <in.pdf>', kind: 'file',
    description: 'Rasterize a local PDF into a new uneditable PDF. Never changes the source.',
    inputSchema: {
      type: 'object', properties: { path: pathSchema, output: newPdfSchema,
        dpi: { type: 'integer', minimum: 36, maximum: 1200, default: 150 }, password: passwordSchema },
      required: ['path', 'output'], additionalProperties: false,
    },
    args: (input, output) => ['--flatten', input.path, output, '--dpi', String(input.dpi ?? 150),
      ...(input.password ? ['--password', input.password] : [])],
  },
  {
    name: 'killerpdf_print', marker: '--print <in.pdf>', kind: 'action',
    description: 'Print selected pages from a local PDF. This sends a real print job to the selected or default printer.',
    inputSchema: {
      type: 'object', properties: { path: pathSchema, printer: { type: 'string', minLength: 1, maxLength: 256 },
        pages: pageRangeSchema, copies: { type: 'integer', minimum: 1, maximum: 99, default: 1 }, password: passwordSchema },
      required: ['path'], additionalProperties: false,
    },
    args: input => ['--print', input.path, ...(input.printer ? ['--printer', input.printer] : []),
      ...(input.pages ? ['--pages', input.pages] : []), '--copies', String(input.copies ?? 1),
      ...(input.password ? ['--password', input.password] : [])],
  },
  {
    name: 'killerpdf_ocr', marker: '--ocr <in.pdf>', kind: 'file', timeout: 600000,
    description: 'Create a new searchable PDF with OCR. A language model may be downloaded on first use. Never changes the source.',
    inputSchema: {
      type: 'object', properties: { path: pathSchema, output: newPdfSchema,
        language: { type: 'string', minLength: 3, maxLength: 32, pattern: '^[A-Za-z0-9_]+$', default: 'eng' }, password: passwordSchema },
      required: ['path', 'output'], additionalProperties: false,
    },
    args: (input, output) => ['--ocr', input.path, output, '--lang', input.language ?? 'eng',
      ...(input.password ? ['--password', input.password] : [])],
  },
  {
    name: 'killerpdf_resave', marker: '--batch-resave <in>', kind: 'file', timeout: 300000,
    description: 'Resave a local PDF through KillerPDF into a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, output: newPdfSchema }, required: ['path', 'output'], additionalProperties: false },
    args: (input, output) => ['--batch-resave', input.path, output, '--quiet'],
  },
  {
    name: 'killerpdf_benchmark_render', marker: '--batch-render <in>', kind: 'directory', timeout: 600000,
    description: 'Render the first pages of a local PDF into PNG files with timing data for comparison and diagnostics.',
    inputSchema: {
      type: 'object', properties: { path: pathSchema, outputFolder: newFolderSchema,
        size: { type: 'integer', minimum: 16, maximum: 8192, default: 1024 },
        pageLimit: { type: 'integer', minimum: 1, maximum: 100, default: 1 } },
      required: ['path', 'outputFolder'], additionalProperties: false,
    },
    args: (input, output) => ['--batch-render', input.path, output, '--size', String(input.size ?? 1024),
      '--pages', String(input.pageLimit ?? 1), '--quiet'],
  },
  {
    name: 'killerpdf_rotate_pages', marker: '--rotate-pages <in.pdf>', kind: 'file',
    description: 'Rotate selected pages clockwise into a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, pages: pageRangeSchema, degrees: { type: 'integer', enum: [90, 180, 270] }, output: newPdfSchema }, required: ['path', 'pages', 'degrees', 'output'], additionalProperties: false },
    args: (input, output) => ['--rotate-pages', input.path, input.pages, String(input.degrees), output],
  },
  {
    name: 'killerpdf_delete_pages', marker: '--delete-pages <in.pdf>', kind: 'file',
    description: 'Remove selected pages into a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, pages: pageRangeSchema, output: newPdfSchema }, required: ['path', 'pages', 'output'], additionalProperties: false },
    args: (input, output) => ['--delete-pages', input.path, input.pages, output],
  },
  {
    name: 'killerpdf_move_pages', marker: '--move-pages <in.pdf>', kind: 'file',
    description: 'Move selected pages before a one-based position in a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, pages: pageRangeSchema, position: { type: 'integer', minimum: 1, maximum: 100000 }, output: newPdfSchema }, required: ['path', 'pages', 'position', 'output'], additionalProperties: false },
    args: (input, output) => ['--move-pages', input.path, input.pages, String(input.position), output],
  },
  {
    name: 'killerpdf_insert_blank_page', marker: '--insert-blank <in.pdf>', kind: 'file',
    description: 'Insert a blank page before a one-based position in a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, position: { type: 'integer', minimum: 1, maximum: 100000 }, width: { type: 'number', exclusiveMinimum: 0, maximum: 14400, default: 612 }, height: { type: 'number', exclusiveMinimum: 0, maximum: 14400, default: 792 }, output: newPdfSchema }, required: ['path', 'position', 'output'], additionalProperties: false },
    args: (input, output) => ['--insert-blank', input.path, String(input.position), output, '--width', String(input.width ?? 612), '--height', String(input.height ?? 792)],
  },
  {
    name: 'killerpdf_duplicate_page', marker: '--duplicate-page <in.pdf>', kind: 'file',
    description: 'Duplicate one page in a new PDF without changing the source.',
    inputSchema: { type: 'object', properties: { path: pathSchema, page: { type: 'integer', minimum: 1, maximum: 100000 }, output: newPdfSchema }, required: ['path', 'page', 'output'], additionalProperties: false },
    args: (input, output) => ['--duplicate-page', input.path, String(input.page), output],
  },
  {
    name: 'killerpdf_document_info', marker: '--document-info <in.pdf>', kind: 'action',
    description: 'Read page count, PDF version, metadata, language, and dates from a local PDF without changing it.',
    inputSchema: { type: 'object', properties: { path: pathSchema }, required: ['path'], additionalProperties: false },
    args: input => ['--document-info', input.path],
  },
  {
    name: 'killerpdf_search_text', marker: '--search-text <in.pdf>', kind: 'action',
    description: 'Search text in a local PDF and return the matching page numbers without changing it.',
    inputSchema: { type: 'object', properties: { path: pathSchema, query: { type: 'string', minLength: 1, maxLength: 512 } }, required: ['path', 'query'], additionalProperties: false },
    args: input => ['--search-text', input.path, input.query],
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

function validateOperation(input, definition) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return 'Expected PDF arguments';
  const allowed = new Set(Object.keys(definition.inputSchema.properties));
  if (Object.keys(input).some(key => !allowed.has(key))) return 'Unknown PDF argument';
  if (typeof input.path !== 'string' || !isAbsolute(input.path) || input.path.length > 1024
    || extname(input.path).toLowerCase() !== '.pdf') return 'Path must name an absolute PDF file';
  try {
    const details = statSync(input.path);
    if (!details.isFile() || details.size > maxPdfBytes) return 'PDF must be a file no larger than 64 MiB';
  }
  catch { return 'Unable to read PDF file'; }
  const destination = definition.kind === 'directory' ? input.outputFolder : input.output;
  if (definition.kind !== 'action') {
    if (typeof destination !== 'string' || !isAbsolute(destination) || destination.length > 1024) return 'Output must be an absolute path';
    if (existsSync(destination)) return 'Output already exists';
    try { if (!statSync(dirname(destination)).isDirectory()) return 'Output folder does not exist'; }
    catch { return 'Output folder does not exist'; }
    if (definition.kind === 'file' && extname(destination).toLowerCase() !== '.pdf') return 'Output must be a PDF path';
  }
  if (input.pages !== undefined && (typeof input.pages !== 'string'
    || !/^\d+(?:-\d+)?(?:,\d+(?:-\d+)?)*$/.test(input.pages) || input.pages.length > 256)) return 'Invalid page range';
  if (input.dpi !== undefined && (!Number.isInteger(input.dpi) || input.dpi < 36 || input.dpi > 1200)) return 'DPI must be between 36 and 1200';
  if (input.copies !== undefined && (!Number.isInteger(input.copies) || input.copies < 1 || input.copies > 99)) return 'Copies must be between 1 and 99';
  if (input.size !== undefined && (!Number.isInteger(input.size) || input.size < 16 || input.size > 8192)) return 'Render size must be between 16 and 8192';
  if (input.pageLimit !== undefined && (!Number.isInteger(input.pageLimit) || input.pageLimit < 1 || input.pageLimit > 100)) return 'Page limit must be between 1 and 100';
  if (input.degrees !== undefined && ![90, 180, 270].includes(input.degrees)) return 'Degrees must be 90, 180, or 270';
  if (input.position !== undefined && (!Number.isInteger(input.position) || input.position < 1 || input.position > 100000)) return 'Position must be a positive page number';
  if (input.page !== undefined && (!Number.isInteger(input.page) || input.page < 1 || input.page > 100000)) return 'Page must be a positive number';
  if (input.width !== undefined && (typeof input.width !== 'number' || input.width <= 0 || input.width > 14400)) return 'Width must be between 0 and 14400 points';
  if (input.height !== undefined && (typeof input.height !== 'number' || input.height <= 0 || input.height > 14400)) return 'Height must be between 0 and 14400 points';
  if (input.query !== undefined && (typeof input.query !== 'string' || input.query.trim().length === 0 || input.query.length > 512)) return 'Query must contain text and be no longer than 512 characters';
  if (input.password !== undefined && (typeof input.password !== 'string' || input.password.length > 1024)) return 'Invalid password';
  return null;
}

function execute(path, args, timeout = 180000) {
  return new Promise(resolve => {
    execFile(path, args, { encoding: 'utf8', windowsHide: true, timeout, maxBuffer: 262144 },
      (error, stdout, stderr) => resolve({ error, stdout, stderr }));
  });
}

async function runOperation(path, definition, input) {
  const problem = validateOperation(input, definition);
  if (problem) return result(problem, true);
  let temporary;
  try {
    if (definition.kind === 'action') {
      const response = await execute(path, definition.args(input), definition.timeout);
      if (response.error) return result((response.stderr || response.stdout || response.error.message).trim().slice(0, 1024), true);
      return result(JSON.stringify({ completed: true, message: response.stdout.trim().slice(0, 1024) }));
    }
    temporary = await mkdtemp(join(tmpdir(), 'killermcp-pdf-'));
    const staged = definition.kind === 'file' ? join(temporary, 'output.pdf') : join(temporary, 'output');
    const response = await execute(path, definition.args(input, staged), definition.timeout);
    if (response.error) return result((response.stderr || response.stdout || response.error.message).trim().slice(0, 1024), true);
    if (!existsSync(staged)) return result('KillerPDF did not create the requested output', true);
    const destination = definition.kind === 'file' ? input.output : input.outputFolder;
    if (definition.kind === 'file') await copyFile(staged, destination, constants.COPYFILE_EXCL);
    else await rename(staged, destination);
    const created = definition.kind === 'directory' ? (await readdir(destination)).length : 1;
    return result(JSON.stringify({ output: destination, created }));
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
  for (const definition of operationTools) {
    if (help.includes(definition.marker)) {
      const { marker, kind, args, timeout, ...tool } = definition;
      adapters.push({ tool, call: input => runOperation(path, definition, input) });
    }
  }
  for (const { command, ...tool } of tools) {
    if (help.includes(`${command} <in.pdf>`)) adapters.push({ tool, call: input => call(path, command, input) });
  }
  return adapters;
}
