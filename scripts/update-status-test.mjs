import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkForUpdate, compareVersions, createUpdateAdapter, updateInstruction } from '../src/update-status.mjs';

assert.equal(compareVersions('0.1.1', '0.1.2'), -1);
assert.equal(compareVersions('0.1.10', '0.1.9'), 1);
assert.equal(compareVersions('v1.0.0', '1.0.0'), 0);
assert.equal(compareVersions('bad', '1.0.0'), null);

const temporary = await mkdtemp(join(tmpdir(), 'killermcp-update-test-'));
let requests = 0;
const server = createServer((request, response) => {
  requests++;
  response.writeHead(200, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify({ tag_name: 'v0.1.2' }));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const address = server.address();
process.env.KILLERMCP_UPDATE_API = `http://127.0.0.1:${address.port}/latest`;
process.env.KILLERMCP_UPDATE_CACHE = join(temporary, 'status.json');

try {
  const first = await checkForUpdate('0.1.1');
  assert.equal(first.updateAvailable, true);
  assert.equal(first.latestVersion, '0.1.2');
  assert.equal(first.cached, false);
  assert.match(updateInstruction(first), /KillerMCP 0\.1\.2 is available/);
  assert.equal(requests, 1);

  const second = await checkForUpdate('0.1.1');
  assert.equal(second.cached, true);
  assert.equal(requests, 1);
  assert.equal(JSON.parse(await readFile(process.env.KILLERMCP_UPDATE_CACHE, 'utf8')).latestVersion, '0.1.2');

  const adapter = await createUpdateAdapter('0.1.1');
  const response = await adapter.call({});
  assert.equal(JSON.parse(response.content[0].text).updateAvailable, true);
  assert.equal((await adapter.call({ force: true })).isError, true);
}
finally {
  server.close();
  await rm(temporary, { recursive: true, force: true });
}

process.stdout.write('KillerMCP update status checks passed\n');
