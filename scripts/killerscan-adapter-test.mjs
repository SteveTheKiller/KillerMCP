import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { isAbsolute } from 'node:path';
import { createKillerScanAdapters } from '../src/apps/killerscan.mjs';

const executable = process.argv[2];
if (!executable || !isAbsolute(executable) || !existsSync(executable)) {
  throw new Error('Pass an absolute KillerScan executable path');
}

const adapters = await createKillerScanAdapters(executable);
const byName = new Map(adapters.map(adapter => [adapter.tool.name, adapter]));
assert.deepEqual([...byName.keys()].sort(), [
  'killerscan_diagnose_host',
  'killerscan_local_network',
  'killerscan_mac_vendor',
  'killerscan_ping',
  'killerscan_probe_host',
  'killerscan_scan_network',
  'killerscan_speed_test',
  'killerscan_trace_route',
  'killerscan_watch_hosts',
]);
assert.equal(byName.get('killerscan_scan_network').tool.inputSchema.required, undefined);

const network = await byName.get('killerscan_local_network').call({});
assert.equal(network.isError, undefined);
assert.match(JSON.parse(network.content[0].text).localIp, /^\d{1,3}(?:\.\d{1,3}){3}$/);

const vendor = await byName.get('killerscan_mac_vendor').call({ mac: '00:00:0C:00:00:00' });
assert.equal(vendor.isError, undefined);
assert.match(JSON.parse(vendor.content[0].text).vendor, /Cisco/i);

assert.equal((await byName.get('killerscan_probe_host').call({ target: 'example.com' })).isError, true);
assert.equal((await byName.get('killerscan_mac_vendor').call({ mac: 'invalid' })).isError, true);
assert.equal((await byName.get('killerscan_ping').call({ target: 'not a host' })).isError, true);
assert.equal((await byName.get('killerscan_trace_route').call({ target: '127.0.0.1', maxHops: 65 })).isError, true);
assert.equal((await byName.get('killerscan_diagnose_host').call({ target: '127.0.0.1', ports: [0] })).isError, true);
assert.equal((await byName.get('killerscan_watch_hosts').call({ targets: ['example.com'] })).isError, true);
assert.equal((await byName.get('killerscan_speed_test').call({ unexpected: true })).isError, true);

process.stdout.write('KillerScan MCP adapter checks passed\n');
