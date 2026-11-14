import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';

export async function runReview(hostPath, scratchPath, mode, fixturePath) {
  const directory = join(resolve(scratchPath), `fixture-${randomUUID()}`);
  await mkdir(directory, { recursive: true });
  const missing = join(directory, 'missing.exe');
  const hosts = [];
  let hits = 0;
  const server = createServer((request, response) => {
    hits++;
    if (mode === 'startup') return;
    response.writeHead(503);
    response.end('unavailable');
  });
  await new Promise(done => server.listen(0, '127.0.0.1', done));
  const address = `http://127.0.0.1:${server.address().port}/latest`;
  let serial = 0;
  const start = () => {
    const child = spawn(resolve(hostPath), [], { stdio: ['pipe', 'pipe', 'pipe'], env: {
      ...process.env,
      KILLERMCP_UPDATE_API: address,
      KILLERMCP_UPDATE_CACHE: join(directory, 'update.json'),
      KILLERMCP_STATE_DIR: join(directory, 'state'),
      KILLERMCP_TOOL_CALL_LOG: join(directory, 'calls.jsonl'),
      ...Object.fromEntries(['KILLERPDF', 'KILLERNOTES', 'KILLERSHELL', 'KILLERSCAN', 'KILLERSHOT', 'KILLENDAR', 'KILLERBENCH'].map(name => [name + '_CLI', missing])),
      ...(mode === 'load' ? { KILLERSHOT_CLI: resolve(fixturePath), KILLERMCP_FAKE_SHOT: '1' } : {}),
    } });
    hosts.push(child);
    let text = '';
    const inbox = [];
    const waiters = [];
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', chunk => {
      text += chunk;
      while (text.includes('\n')) {
        const index = text.indexOf('\n');
        const reply = JSON.parse(text.slice(0, index));
        text = text.slice(index + 1);
        if (waiters.length) waiters.shift()(reply);
        else inbox.push(reply);
      }
    });
    const next = () => new Promise((done, fail) => {
      if (inbox.length) return done(inbox.shift());
      const timer = setTimeout(() => fail(new Error('No protocol reply within five seconds.')), 5000);
      waiters.push(value => { clearTimeout(timer); done(value); });
    });
    const send = value => child.stdin.write(typeof value === 'string' ? value + '\n' : JSON.stringify(value) + '\n');
    const request = async (method, params = {}) => { send({ jsonrpc: '2.0', id: ++serial, method, params }); return await next(); };
    return { child, next, send, request };
  };
  try {
    const host = start();
    if (mode === 'load') {
      await host.request('initialize');
      const tools = await host.request('tools/list');
      assert.ok(tools.result.tools.some(tool => tool.name === 'killershot_capture_all'));
      await host.request('tools/call', { name: 'encode_base64', arguments: { text: 'warmup' } });
      const slowIds = [];
      for (let index = 0; index < 32; index++) {
        const id = ++serial;
        slowIds.push(id);
        host.send({ jsonrpc: '2.0', id, method: 'tools/call', params: { name: 'killershot_capture_all', arguments: { delay: 10 } } });
      }
      await new Promise(done => setTimeout(done, 2000));
      const started = performance.now();
      const fast = await host.request('tools/call', { name: 'encode_base64', arguments: { text: 'ready' } });
      console.log(`Load builtin answered in ${(performance.now() - started).toFixed(1)} ms`);
      assert.equal(fast.error, undefined, 'A local builtin was refused behind 32 app calls.');
      assert.ok(performance.now() - started < 100, 'A local builtin waited more than 100 ms behind 32 app calls.');
      assert.equal(JSON.parse(fast.result.content[0].text).encoded, 'cmVhZHk=');
      assert.deepEqual((await host.request('ping')).result, {});
      for (const requestId of slowIds) host.send({ jsonrpc: '2.0', method: 'notifications/cancelled', params: { requestId } });
      await new Promise(done => setTimeout(done, 400));
      assert.deepEqual((await host.request('ping')).result, {}, 'A cancelled app call received a reply.');
      console.log('PASS actual host builtin under 32 app calls, ping and cancellation (3 selected regressions)');
    } else if (mode === 'startup') {
      await new Promise(done => setTimeout(done, 150));
      const started = performance.now();
      assert.equal((await host.request('initialize')).result.serverInfo.name, 'KillerMCP');
      const elapsed = performance.now() - started;
      assert.ok(elapsed < 300, `Initialize waited ${elapsed.toFixed(1)} ms for the update network.`);
      console.log(`PASS initialize with stalled update server: ${elapsed.toFixed(1)} ms (1 selected regression)`);
    } else if (mode === 'cache') {
      await host.request('initialize');
      await new Promise(done => setTimeout(done, 300));
      const firstHits = hits;
      assert.equal(firstHits, 1);
      const second = start();
      await second.request('initialize');
      await new Promise(done => setTimeout(done, 300));
      assert.equal(hits, firstHits, 'A second host retried a failed update check within 15 minutes.');
      const cache = JSON.parse(await readFile(join(directory, 'update.json'), 'utf8'));
      cache.CheckedAt = new Date(Date.now() - 16 * 60 * 1000).toISOString();
      await writeFile(join(directory, 'update.json'), JSON.stringify(cache));
      const third = start();
      await third.request('initialize');
      await new Promise(done => setTimeout(done, 300));
      assert.equal(hits, firstHits + 1, 'An expired failed update cache prevented retry.');
      console.log('PASS failed update cache avoids retries and expires (2 selected regressions)');
    } else {
      await host.request('initialize');
      host.send('{');
      const malformed = await host.next();
      assert.equal(malformed.error.code, -32700);
      assert.equal(malformed.id, null);
      console.log('PASS malformed JSON receives -32700');
      for (const invalid of [[], 1, 'text', null]) {
        host.send(JSON.stringify(invalid));
        const reply = await host.next();
        assert.equal(reply.error.code, -32600);
        assert.equal(reply.id, null);
      }
      console.log('PASS nonobject messages receive -32600');
      const unknown = await host.request('tools/call', { name: 'no_such_tool', arguments: {} });
      assert.equal(unknown.error.code, -32602);
      host.send({ jsonrpc: '2.0', id: 'client-response', result: {} });
      const ping = await host.request('ping');
      assert.deepEqual(ping.result, {});
      assert.notEqual(ping.id, 'client-response');
      console.log('PASS parse errors, invalid values, unknown tools and ignored client responses (7 selected regressions)');
    }
  } finally {
    for (const child of hosts) {
      child.stdin.end();
      if (child.exitCode === null) {
        await Promise.race([new Promise(done => child.once('exit', done)), new Promise(done => setTimeout(done, 1500))]);
        if (child.exitCode === null) {
          if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
          else child.kill();
        }
      }
    }
    server.closeAllConnections();
    await new Promise(done => server.close(done));
  }
}
