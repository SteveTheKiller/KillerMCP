import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import { supportsWindowsFileVersion } from './app-version.mjs';

const tool = {
  name: 'killendar_agenda',
  description: 'Read appointments from the active Killendar calendar, including recurring events, without changing the calendar. Use for requests such as "killer what is on my calendar this week". Encrypted calendars currently require an app unlock path and are unavailable through this tool.',
  inputSchema: {
    type: 'object',
    properties: {
      date: { type: 'string', description: 'Start date in YYYY-MM-DD format' },
      days: { type: 'integer', minimum: 1, maximum: 31, default: 7 },
      limit: { type: 'integer', minimum: 1, maximum: 100, default: 50 },
    },
    required: ['date'],
    additionalProperties: false,
  },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function cliArgs(path, args) {
  return path.toLowerCase().endsWith('killendar.exe') ? ['--cli', ...args] : args;
}

function validDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}

function call(path, input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !['date', 'days', 'limit'].includes(key))
    || !validDate(input.date)
    || (input.days !== undefined && (!Number.isInteger(input.days) || input.days < 1 || input.days > 31))
    || (input.limit !== undefined && (!Number.isInteger(input.limit) || input.limit < 1 || input.limit > 100))) {
    return Promise.resolve(result('Provide a valid date, up to 31 days, and a limit up to 100 events', true));
  }
  return new Promise(resolve => {
    execFile(path, cliArgs(path, ['agenda', input.date, String(input.days ?? 7), '--limit', String(input.limit ?? 50)]),
      { encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 262144 },
      (error, stdout, stderr) => {
        if (error) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const events = JSON.parse(stdout);
          if (!Array.isArray(events) || events.length > 100) throw new Error('Invalid agenda response');
          resolve(result(JSON.stringify(events)));
        }
        catch { resolve(result('Killendar returned an invalid agenda response', true)); }
      });
  });
}

export async function createKillendarAdapter(path) {
  try {
    if (!path || !isAbsolute(path) || !existsSync(path) || !statSync(path).isFile()) return null;
  }
  catch { return null; }
  if (!await supportsWindowsFileVersion(path, '1.1.4')) return null;
  const help = await new Promise(resolve => {
    execFile(path, cliArgs(path, ['--help']), { encoding: 'utf8', windowsHide: true, timeout: 8000, maxBuffer: 8192 },
      (error, stdout) => resolve(error ? '' : stdout));
  });
  return help.includes('agenda <yyyy-MM-dd>') ? { tool, call: input => call(path, input) } : null;
}
