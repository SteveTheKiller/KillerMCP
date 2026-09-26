import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { discoverAppCli } from '../src/discovery.mjs';

const root = await mkdtemp(join(tmpdir(), 'killermcp-discovery-'));
try {
  const local = join(root, 'Local');
  const machine = join(root, 'Machine');
  const pdf = join(local, 'Programs', 'KillerPDF', 'KillerPDF.App.exe');
  const scan = join(machine, 'KillerScan', 'KillerScan.exe');
  const shell = join(local, 'Programs', 'KillerShell', 'KillerShell.Cli.exe');
  const bench = join(machine, 'KillerBench', 'killerbench-cli.exe');
  const notes = join(machine, 'KillerNotes', 'KillerNotes.exe');
  const killendar = join(local, 'Programs', 'Killendar', 'Killendar.exe');
  for (const path of [pdf, scan, shell, bench, notes, killendar]) {
    await mkdir(join(path, '..'), { recursive: true });
    await writeFile(path, 'fixture');
  }
  const environment = { LOCALAPPDATA: local, ProgramFiles: machine };
  assert.equal(discoverAppCli('killerpdf', environment), pdf);
  assert.equal(discoverAppCli('killerscan', environment), scan);
  assert.equal(discoverAppCli('killershell', environment), shell);
  assert.equal(discoverAppCli('killerbench', environment), bench);
  assert.equal(discoverAppCli('killernotes', environment), notes);
  assert.equal(discoverAppCli('killendar', environment), killendar);
  assert.equal(discoverAppCli('killerpdf', { ...environment, KILLERPDF_CLI: pdf }), pdf);
  assert.equal(discoverAppCli('killerscan', { ...environment, KILLERSCAN_CLI: scan }), scan);
  assert.equal(discoverAppCli('killerpdf', { ...environment, KILLERPDF_CLI: join(root, 'missing.exe') }), null);
  assert.throws(() => discoverAppCli('unknown', environment), /Unknown app/);
  process.stdout.write('App executable discovery passed\n');
}
finally {
  await rm(root, { recursive: true, force: true });
}
