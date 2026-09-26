import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { copyFile, mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const nodeVersion = '24.14.1';
if (process.platform !== 'win32' || process.arch !== 'x64' || process.versions.node !== nodeVersion) {
  throw new Error(`Build the Windows x64 portable package with Node ${nodeVersion}`);
}

const root = fileURLToPath(new URL('../', import.meta.url));
const staged = join(root, 'dist');
const output = join(staged, 'portable');
const stagedManifest = JSON.parse(await readFile(join(staged, 'manifest.json'), 'utf8'));

async function sha256(path) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

const files = Object.entries(stagedManifest.files).map(([name, details]) => ({
  name, source: join(staged, name), expected: details.sha256,
}));
files.push({ name: 'node.exe', source: process.execPath });
await mkdir(output, { recursive: true });
const manifest = { nodeVersion, platform: 'win32', arch: 'x64', files: {} };
for (const file of files) {
  const destination = join(output, file.name);
  await mkdir(dirname(destination), { recursive: true });
  const sourceHash = await sha256(file.source);
  if (file.expected && sourceHash !== file.expected) {
    throw new Error(`Staged file changed after build: ${file.name}`);
  }
  await copyFile(file.source, destination);
  const copiedHash = await sha256(destination);
  if (copiedHash !== sourceHash) throw new Error(`Portable copy failed verification: ${file.name}`);
  manifest.files[file.name] = { bytes: (await stat(destination)).size, sha256: copiedHash };
}

const licenseUrl = `https://raw.githubusercontent.com/nodejs/node/v${nodeVersion}/LICENSE`;
const response = await fetch(licenseUrl, { signal: AbortSignal.timeout(20000) });
if (!response.ok) throw new Error(`Unable to fetch the Node license: HTTP ${response.status}`);
const license = await response.text();
if (!license.startsWith('Node.js is licensed for use as follows:') || /[\u2013\u2014]/.test(license)) {
  throw new Error('The downloaded Node license did not pass validation');
}
const licensePath = join(output, 'LICENSE.node.txt');
await writeFile(licensePath, license);
manifest.files['LICENSE.node.txt'] = { bytes: (await stat(licensePath)).size, sha256: await sha256(licensePath) };
await writeFile(join(output, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
process.stdout.write(`KillerMCP portable development package built at ${output}\n`);
