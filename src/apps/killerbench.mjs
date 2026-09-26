import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isAbsolute } from 'node:path';

const codeSchema = {
  type: 'object',
  properties: { code: { type: 'string', pattern: '^(?:[0-9]{1,10}|0[xX][0-9a-fA-F]{1,8})$' } },
  required: ['code'],
  additionalProperties: false,
};

function tool(name, description) {
  return { name, description, inputSchema: codeSchema };
}

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function validCode(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input) || Object.keys(input).some(key => key !== 'code')) return false;
  if (typeof input.code !== 'string' || !/^(?:[0-9]{1,10}|0x[0-9a-f]{1,8})$/i.test(input.code)) return false;
  const value = input.code.toLowerCase().startsWith('0x') ? Number.parseInt(input.code.slice(2), 16) : Number(input.code);
  return Number.isInteger(value) && value <= 0xFFFFFFFF;
}

function call(path, command, input) {
  if (!validCode(input)) return Promise.resolve(result('Code must be an unsigned decimal or 0x hexadecimal value', true));
  return new Promise(resolve => {
    execFile(path, [command, input.code], { encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 65536 },
      (error, stdout, stderr) => {
        if (error && error.code !== 3) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const parsed = JSON.parse(stdout);
          if (parsed.code === undefined) throw new Error('Invalid lookup response');
          resolve(result(JSON.stringify(parsed)));
        }
        catch {
          resolve(result('KillerBench returned an invalid lookup response', true));
        }
      });
  });
}

export function createKillerBenchAdapters(path) {
  let available = false;
  try {
    available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile());
  }
  catch { /* An inaccessible CLI cannot be offered as a tool. */ }
  if (!available) return [];
  return [
    {
      tool: tool('killerbench_device_code', 'Look up a Device Manager problem code in KillerBench without changing the device.'),
      call: input => call(path, 'device-code', input),
    },
    {
      tool: tool('killerbench_win32_code', 'Look up a Windows system error code in KillerBench without changing the system.'),
      call: input => call(path, 'win32-code', input),
    },
  ];
}
