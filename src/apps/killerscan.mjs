import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isIP } from 'node:net';
import { isAbsolute } from 'node:path';

const tool = {
  name: 'killerscan_local_network',
  description: 'Use KillerScan to identify the active local IPv4 network, interface, gateway, and DNS server without scanning hosts.',
  inputSchema: { type: 'object', properties: {}, additionalProperties: false },
};
const scanTool = {
  name: 'killerscan_scan_network',
  description: 'Scan an IPv4 host or CIDR with KillerScan and return discovered devices as JSON. Use for requests such as "killerscan 192.168.8.0/24". Runs quick discovery by default; set full for fingerprinting and port checks. Scanning sends network probes.',
  inputSchema: {
    type: 'object',
    properties: {
      target: { type: 'string', description: 'IPv4 address or CIDR with at most 1024 addresses' },
      full: { type: 'boolean', default: false },
      limit: { type: 'integer', minimum: 1, maximum: 100, default: 100 },
    },
    required: ['target'],
    additionalProperties: false,
  },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

function createLocalNetworkAdapter(path) {
  return {
    tool,
    call(input) {
      if (!input || typeof input !== 'object' || Array.isArray(input) || Object.keys(input).length) {
        return Promise.resolve(result('This tool takes no arguments', true));
      }
      return new Promise(resolve => {
        execFile(path, ['/network'], { encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 8192 },
          (error, stdout, stderr) => {
            if (error) {
              resolve(result((stderr || error.message).trim().slice(0, 1024), true));
              return;
            }
            const labels = new Map([
              ['INTERFACE', 'interface'], ['LOCAL IP', 'localIp'], ['SUBNET', 'subnet'],
              ['GATEWAY', 'gateway'], ['DNS', 'dns'],
            ]);
            const network = {};
            for (const line of stdout.split(/\r?\n/)) {
              const match = line.match(/^(INTERFACE|LOCAL IP|SUBNET|GATEWAY|DNS)\s{2,}(.+)$/);
              if (match) network[labels.get(match[1])] = match[2];
            }
            if (Object.keys(network).length !== labels.size) {
              resolve(result('KillerScan returned an incomplete network response', true));
              return;
            }
            resolve(result(JSON.stringify(network)));
          });
      });
    },
  };
}

function validateScan(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !['target', 'full', 'limit'].includes(key))) return 'Expected scan arguments';
  if (typeof input.target !== 'string' || input.target.length > 43) return 'Target must be an IPv4 host or CIDR';
  const parts = input.target.split('/');
  if (parts.length > 2 || isIP(parts[0]) !== 4
    || (parts.length === 2 && (!/^\d{1,2}$/.test(parts[1]) || Number(parts[1]) < 22 || Number(parts[1]) > 32))) {
    return 'Target must be an IPv4 host or CIDR with at most 1024 addresses';
  }
  if (input.full !== undefined && typeof input.full !== 'boolean') return 'Full must be true or false';
  if (input.limit !== undefined && (!Number.isInteger(input.limit) || input.limit < 1 || input.limit > 100)) {
    return 'Limit must be between 1 and 100';
  }
  return null;
}

function scan(path, input) {
  const problem = validateScan(input);
  if (problem) return Promise.resolve(result(problem, true));
  const args = ['/scan', input.target, '/json', '/timeout', '60', '/limit', String(input.limit ?? 100)];
  if (!input.full) args.push('/quick');
  return new Promise(resolve => {
    execFile(path, args, { encoding: 'utf8', windowsHide: true, timeout: 70000, maxBuffer: 1048576 },
      (error, stdout, stderr) => {
        if (error) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const devices = JSON.parse(stdout);
          if (!Array.isArray(devices) || devices.length > 100) throw new Error('Invalid scan response');
          resolve(result(JSON.stringify(devices)));
        }
        catch { resolve(result('KillerScan returned an invalid scan response', true)); }
      });
  });
}

export async function createKillerScanAdapters(path) {
  let available = false;
  try { available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile()); }
  catch { /* An inaccessible CLI cannot be offered as a tool. */ }
  if (!available) return [];
  const help = await new Promise(resolve => {
    execFile(path, ['/help'], { encoding: 'utf8', windowsHide: true, timeout: 8000, maxBuffer: 65536 },
      (error, stdout) => resolve(error ? '' : stdout));
  });
  const adapters = [];
  if (help.includes('/network')) adapters.push(createLocalNetworkAdapter(path));
  if (help.includes('/scan [targets]')) adapters.push({ tool: scanTool, call: input => scan(path, input) });
  return adapters;
}
