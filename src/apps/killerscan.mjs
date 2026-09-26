import { execFile } from 'node:child_process';
import { existsSync, statSync } from 'node:fs';
import { isAbsolute } from 'node:path';

const tool = {
  name: 'killerscan_local_network',
  description: 'Use KillerScan to identify the active local IPv4 network, interface, gateway, and DNS server without scanning hosts.',
  inputSchema: { type: 'object', properties: {}, additionalProperties: false },
};

function result(message, isError = false) {
  return { content: [{ type: 'text', text: message }], ...(isError ? { isError: true } : {}) };
}

export function createKillerScanAdapter(path) {
  let available = false;
  try {
    available = Boolean(path && isAbsolute(path) && existsSync(path) && statSync(path).isFile());
  }
  catch { /* An inaccessible CLI cannot be offered as a tool. */ }
  if (!available) return null;
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
