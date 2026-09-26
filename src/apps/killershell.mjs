import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isAbsolute } from 'node:path';

const name = 'killershell_search_files';
const tool = {
  name,
  description: 'Search local files with KillerShell by filename, file content, or both. Returns bounded JSON results.',
  inputSchema: {
    type: 'object',
    properties: {
      root: { type: 'string', minLength: 1, maxLength: 1024, description: 'Absolute directory path to search' },
      name: { type: 'string', minLength: 1, maxLength: 256, description: 'Filename text or wildcard pattern' },
      content: { type: 'string', minLength: 1, maxLength: 512, description: 'Text to find inside files' },
      limit: { type: 'integer', minimum: 1, maximum: 100, default: 100 },
    },
    required: ['root'],
    additionalProperties: false,
  },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function validate(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return 'Expected search arguments';
  if (typeof input.root !== 'string' || !isAbsolute(input.root) || input.root.length > 1024) {
    return 'Root must be an absolute directory path';
  }
  for (const key of ['name', 'content']) {
    if (input[key] !== undefined && (typeof input[key] !== 'string' || !input[key].trim()
      || input[key].length > (key === 'name' ? 256 : 512))) return `Invalid ${key}`;
  }
  if (!input.name && !input.content) return 'Provide a name or content search';
  if (input.limit !== undefined && (!Number.isInteger(input.limit) || input.limit < 1 || input.limit > 100)) {
    return 'Limit must be between 1 and 100';
  }
  if (Object.keys(input).some(key => !['root', 'name', 'content', 'limit'].includes(key))) {
    return 'Unknown search argument';
  }
  return null;
}

export function createKillerShellAdapter(path) {
  let available = false;
  try {
    available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile());
  }
  catch { /* An inaccessible CLI cannot be offered as a tool. */ }
  if (!available) return null;
  return {
    tool,
    call(input) {
      const problem = validate(input);
      if (problem) return Promise.resolve(result(problem, true));
      const args = ['search', input.root];
      if (input.name) args.push('--name', input.name);
      if (input.content) args.push('--content', input.content);
      args.push('--limit', String(input.limit ?? 100));
      return new Promise(resolve => {
        execFile(path, args, { encoding: 'utf8', windowsHide: true, timeout: 30000, maxBuffer: 262144 },
          (error, stdout, stderr) => {
            if (error) {
              resolve(result((stderr || error.message).trim().slice(0, 1024), true));
              return;
            }
            try {
              const parsed = JSON.parse(stdout);
              if (!Array.isArray(parsed.results)) throw new Error('Invalid search response');
              resolve(result(JSON.stringify(parsed)));
            }
            catch {
              resolve(result('KillerShell returned an invalid search response', true));
            }
          });
      });
    },
  };
}
