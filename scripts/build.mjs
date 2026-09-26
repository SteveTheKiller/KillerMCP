import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { copyFile, mkdir, stat, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const source = process.argv[2];
if (!source || !isAbsolute(source)) {
  throw new Error('Pass the absolute path to the built KillerTools local MCP bundle');
}

const root = fileURLToPath(new URL('../', import.meta.url));
const output = join(root, 'dist');
const files = [
  { source: join(root, 'src', 'server.mjs'), name: 'killermcp.mjs' },
  { source: join(root, 'src', 'apps', 'killershell.mjs'), name: 'apps/killershell.mjs' },
  { source: resolve(source), name: 'killertools.mjs' },
];

async function sha256(path) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

await mkdir(output, { recursive: true });
const manifest = { files: {} };
for (const file of files) {
  const destination = join(output, file.name);
  await mkdir(dirname(destination), { recursive: true });
  await copyFile(file.source, destination);
  const [sourceDetails, copiedDetails, sourceHash, copiedHash] = await Promise.all([
    stat(file.source), stat(destination), sha256(file.source), sha256(destination),
  ]);
  if (sourceDetails.size !== copiedDetails.size || sourceHash !== copiedHash) {
    throw new Error(`Package copy failed verification: ${file.name}`);
  }
  manifest.files[file.name] = { bytes: copiedDetails.size, sha256: copiedHash };
}
await writeFile(join(output, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
process.stdout.write(`KillerMCP runtime staged at ${output}\n`);
