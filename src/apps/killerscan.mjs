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
  description: 'Scan the active local IPv4 network, an IPv4 host, or a CIDR with KillerScan and return discovered devices as JSON. Omit target for requests such as "killerscan my network". Runs quick discovery by default; set full for fingerprinting and port checks. Scanning sends network probes.',
  inputSchema: {
    type: 'object',
    properties: {
      target: { type: 'string', description: 'IPv4 address or CIDR with at most 1024 addresses. Omit to scan the active local subnet.' },
      full: { type: 'boolean', default: false },
      limit: { type: 'integer', minimum: 1, maximum: 100, default: 100 },
    },
    additionalProperties: false,
  },
};
const probeTool = {
  name: 'killerscan_probe_host',
  description: 'Deep probe one IPv4 host with KillerScan, including ports 1 through 1024, and return its device details as JSON. Probing sends network traffic to the selected host.',
  inputSchema: {
    type: 'object',
    properties: {
      target: { type: 'string', description: 'One IPv4 address' },
      timeout: { type: 'integer', minimum: 5, maximum: 300, default: 60 },
    },
    required: ['target'],
    additionalProperties: false,
  },
};
const vendorTool = {
  name: 'killerscan_mac_vendor',
  description: 'Look up the manufacturer of a MAC address in KillerScan\'s bundled offline OUI database.',
  inputSchema: {
    type: 'object',
    properties: { mac: { type: 'string', description: 'A 12 digit MAC address in any common separator format' } },
    required: ['mac'],
    additionalProperties: false,
  },
};
const pingTool = {
  name: 'killerscan_ping',
  description: 'Send a bounded set of ICMP checks to one IPv4 address or hostname with KillerScan and return latency and packet loss as JSON.',
  inputSchema: { type: 'object', properties: { target: { type: 'string' }, count: { type: 'integer', minimum: 1, maximum: 20, default: 4 } }, required: ['target'], additionalProperties: false },
};
const traceTool = {
  name: 'killerscan_trace_route',
  description: 'Trace the network path to one IPv4 address or hostname with KillerScan and return up to 64 hops as JSON.',
  inputSchema: { type: 'object', properties: { target: { type: 'string' }, maxHops: { type: 'integer', minimum: 1, maximum: 64, default: 30 } }, required: ['target'], additionalProperties: false },
};
const diagnoseTool = {
  name: 'killerscan_diagnose_host',
  description: 'Check DNS, ping, route selection, and selected TCP ports for one IPv4 address or hostname with KillerScan.',
  inputSchema: { type: 'object', properties: { target: { type: 'string' }, ports: { type: 'array', items: { type: 'integer', minimum: 1, maximum: 65535 }, maxItems: 32 } }, required: ['target'], additionalProperties: false },
};
const watchTool = {
  name: 'killerscan_watch_hosts',
  description: 'Sample availability and latency for 1 to 16 IPv4 addresses with KillerScan. Sends repeated ICMP checks for the requested bounded interval.',
  inputSchema: { type: 'object', properties: { targets: { type: 'array', items: { type: 'string' }, minItems: 1, maxItems: 16 }, count: { type: 'integer', minimum: 1, maximum: 20, default: 4 }, interval: { type: 'integer', minimum: 1, maximum: 10, default: 1 } }, required: ['targets'], additionalProperties: false },
};
const speedTestTool = {
  name: 'killerscan_speed_test',
  description: 'Run KillerScan\'s native KillerSpeed test. This contacts speed.killerscan.net and can transfer up to 6 GiB of generated test data plus network overhead.',
  inputSchema: { type: 'object', properties: {}, additionalProperties: false },
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
  if (input.target !== undefined) {
    if (typeof input.target !== 'string' || input.target.length > 43) return 'Target must be an IPv4 host or CIDR';
    const parts = input.target.split('/');
    if (parts.length > 2 || isIP(parts[0]) !== 4
      || (parts.length === 2 && (!/^\d{1,2}$/.test(parts[1]) || Number(parts[1]) < 22 || Number(parts[1]) > 32))) {
      return 'Target must be an IPv4 host or CIDR with at most 1024 addresses';
    }
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
  const args = ['/scan'];
  if (input.target !== undefined) args.push(input.target);
  args.push('/json', '/timeout', '60', '/limit', String(input.limit ?? 100));
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

function probe(path, input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !['target', 'timeout'].includes(key))
    || typeof input.target !== 'string' || isIP(input.target) !== 4) {
    return Promise.resolve(result('Target must be one IPv4 address', true));
  }
  if (input.timeout !== undefined && (!Number.isInteger(input.timeout) || input.timeout < 5 || input.timeout > 300)) {
    return Promise.resolve(result('Timeout must be between 5 and 300 seconds', true));
  }
  const timeout = input.timeout ?? 60;
  const args = ['/probe', input.target, '/json', '/timeout', String(timeout), '/limit', '1'];
  return new Promise(resolve => {
    execFile(path, args, { encoding: 'utf8', windowsHide: true, timeout: (timeout + 10) * 1000, maxBuffer: 1048576 },
      (error, stdout, stderr) => {
        if (error) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        try {
          const devices = JSON.parse(stdout);
          if (!Array.isArray(devices) || devices.length > 1) throw new Error('Invalid probe response');
          resolve(result(JSON.stringify(devices)));
        }
        catch { resolve(result('KillerScan returned an invalid probe response', true)); }
      });
  });
}

function vendor(path, input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => key !== 'mac') || typeof input.mac !== 'string'
    || input.mac.replace(/[^0-9a-f]/gi, '').length !== 12) {
    return Promise.resolve(result('MAC address must contain 12 hexadecimal digits', true));
  }
  return new Promise(resolve => {
    execFile(path, ['/vendor', input.mac], { encoding: 'utf8', windowsHide: true, timeout: 15000, maxBuffer: 8192 },
      (error, stdout, stderr) => {
        const value = stdout.trim();
        if (error && !value) {
          resolve(result((stderr || error.message).trim().slice(0, 1024), true));
          return;
        }
        resolve(result(JSON.stringify({ mac: input.mac, vendor: value || 'Unknown' })));
      });
  });
}

function validTarget(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 253
    && (isIP(value) === 4 || /^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/i.test(value));
}

function jsonCommand(path, input, allowed, args, timeout, validate) {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(key => !allowed.includes(key))) return Promise.resolve(result('Invalid arguments', true));
  const problem = validate(input);
  if (problem) return Promise.resolve(result(problem, true));
  return new Promise(resolve => {
    execFile(path, args(input), { encoding: 'utf8', windowsHide: true, timeout, maxBuffer: 1048576 },
      (error, stdout, stderr) => {
        if (error) { resolve(result((stderr || error.message).trim().slice(0, 1024), true)); return; }
        try { resolve(result(JSON.stringify(JSON.parse(stdout)))); }
        catch { resolve(result('KillerScan returned invalid JSON', true)); }
      });
  });
}

function ping(path, input) {
  return jsonCommand(path, input, ['target', 'count'], value => ['/ping', value.target, '/count', String(value.count ?? 4), '/json'], 120000, value =>
    !validTarget(value.target) ? 'Target must be one IPv4 address or hostname' : value.count !== undefined && (!Number.isInteger(value.count) || value.count < 1 || value.count > 20) ? 'Count must be between 1 and 20' : null);
}

function trace(path, input) {
  return jsonCommand(path, input, ['target', 'maxHops'], value => ['/trace', value.target, '/max-hops', String(value.maxHops ?? 30), '/json'], 180000, value =>
    !validTarget(value.target) ? 'Target must be one IPv4 address or hostname' : value.maxHops !== undefined && (!Number.isInteger(value.maxHops) || value.maxHops < 1 || value.maxHops > 64) ? 'Max hops must be between 1 and 64' : null);
}

function diagnose(path, input) {
  return jsonCommand(path, input, ['target', 'ports'], value => ['/diagnose', value.target, ...(value.ports?.length ? ['/ports', value.ports.join(',')] : []), '/json'], 120000, value => {
    if (!validTarget(value.target)) return 'Target must be one IPv4 address or hostname';
    if (value.ports !== undefined && (!Array.isArray(value.ports) || value.ports.length > 32 || value.ports.some(port => !Number.isInteger(port) || port < 1 || port > 65535))) return 'Ports must contain up to 32 port numbers from 1 to 65535';
    return null;
  });
}

function watch(path, input) {
  return jsonCommand(path, input, ['targets', 'count', 'interval'], value => ['/watch', ...value.targets, '/count', String(value.count ?? 4), '/interval', String(value.interval ?? 1), '/json'], 220000, value => {
    if (!Array.isArray(value.targets) || value.targets.length < 1 || value.targets.length > 16 || value.targets.some(target => isIP(target) !== 4)) return 'Targets must contain 1 to 16 IPv4 addresses';
    if (value.count !== undefined && (!Number.isInteger(value.count) || value.count < 1 || value.count > 20)) return 'Count must be between 1 and 20';
    if (value.interval !== undefined && (!Number.isInteger(value.interval) || value.interval < 1 || value.interval > 10)) return 'Interval must be between 1 and 10 seconds';
    return null;
  });
}

function speedTest(path, input) {
  return jsonCommand(path, input, [], () => ['/speedtest', '/json'], 120000, () => null);
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
  if (help.includes('/probe <IPv4>')) adapters.push({ tool: probeTool, call: input => probe(path, input) });
  if (help.includes('/vendor <MAC>')) adapters.push({ tool: vendorTool, call: input => vendor(path, input) });
  if (help.includes('/ping <target>')) adapters.push({ tool: pingTool, call: input => ping(path, input) });
  if (help.includes('/trace <target>')) adapters.push({ tool: traceTool, call: input => trace(path, input) });
  if (help.includes('/diagnose <target>')) adapters.push({ tool: diagnoseTool, call: input => diagnose(path, input) });
  if (help.includes('/watch <targets>')) adapters.push({ tool: watchTool, call: input => watch(path, input) });
  if (help.includes('/speedtest')) adapters.push({ tool: speedTestTool, call: input => speedTest(path, input) });
  return adapters;
}
