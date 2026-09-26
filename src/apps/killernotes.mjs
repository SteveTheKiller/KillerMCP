import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isAbsolute } from 'node:path';

const tool = {
  name: 'killernotes_search',
  description: 'Search the active KillerNotes database for note titles, tags, and text snippets without changing notes. Use for requests such as "killer find my notes about subnet plans". Encrypted databases currently require an app unlock path and are unavailable through this tool.',
  inputSchema: {
    type: 'object',
    properties: {
      query: { type: 'string', minLength: 1, maxLength: 200 },
      limit: { type: 'integer', minimum: 1, maximum: 20, default: 10 },
    },
    required: ['query'],
    additionalProperties: false,
  },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function call(path, input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !['query', 'limit'].includes(key))
    || typeof input.query !== 'string' || !input.query.trim() || input.query.length > 200
    || (input.limit !== undefined && (!Number.isInteger(input.limit) || input.limit < 1 || input.limit > 20))) {
    return Promise.resolve(result('Provide a search query up to 200 characters and optional limit from 1 to 20', true));
  }
  return new Promise(resolve => {
    execFile(path, ['search', input.query, '--limit', String(input.limit ?? 10)],
      { encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 65536 },
      (error, stdout, stderr) => {
        if (error) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const matches = JSON.parse(stdout);
          if (!Array.isArray(matches) || matches.length > 20) throw new Error('Invalid search response');
          resolve(result(JSON.stringify(matches)));
        }
        catch { resolve(result('KillerNotes returned an invalid search response', true)); }
      });
  });
}

export async function createKillerNotesAdapter(path) {
  try {
    if (!path || !isAbsolute(path) || !existsSync(path) || !statSync(path).isFile()) return null;
  }
  catch { return null; }
  const help = await new Promise(resolve => {
    execFile(path, ['--help'], { encoding: 'utf8', windowsHide: true, timeout: 8000, maxBuffer: 8192 },
      (error, stdout) => resolve(error ? '' : stdout));
  });
  return help.includes('search <query>') ? { tool, call: input => call(path, input) } : null;
}
