import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import process from 'node:process';

const releaseApi = 'https://api.github.com/repos/SteveTheKiller/KillerMCP/releases/latest';
const installerUrl = 'https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe';
const releaseUrl = 'https://github.com/SteveTheKiller/KillerMCP/releases/latest';
const cacheLifetime = 24 * 60 * 60 * 1000;

function cachePath() {
  if (process.env.KILLERMCP_UPDATE_CACHE) return process.env.KILLERMCP_UPDATE_CACHE;
  const base = process.env.LOCALAPPDATA || join(homedir(), '.local', 'share');
  return join(base, 'KillerMCP', 'update-status.json');
}

function parts(version) {
  const match = String(version).trim().replace(/^v/i, '').match(/^(\d+)\.(\d+)\.(\d+)$/);
  return match ? match.slice(1).map(Number) : null;
}

export function compareVersions(left, right) {
  const a = parts(left);
  const b = parts(right);
  if (!a || !b) return null;
  for (let index = 0; index < 3; index++) {
    if (a[index] !== b[index]) return a[index] < b[index] ? -1 : 1;
  }
  return 0;
}

async function readCache(path, currentVersion) {
  try {
    const value = JSON.parse(await readFile(path, 'utf8'));
    const checked = Date.parse(value.checkedAt);
    if (!Number.isFinite(checked) || Date.now() - checked >= cacheLifetime || typeof value.latestVersion !== 'string') return null;
    return makeStatus(currentVersion, value.latestVersion, value.checkedAt, true);
  }
  catch { return null; }
}

function makeStatus(currentVersion, latestVersion, checkedAt, cached) {
  const order = compareVersions(currentVersion, latestVersion);
  return {
    installedVersion: currentVersion,
    latestVersion,
    updateAvailable: order === -1,
    checkedAt,
    cached,
    installerUrl,
    releaseUrl,
  };
}

export async function checkForUpdate(currentVersion) {
  const path = cachePath();
  const cached = await readCache(path, currentVersion);
  if (cached) return cached;
  try {
    const endpoint = process.env.KILLERMCP_UPDATE_API || releaseApi;
    const response = await fetch(endpoint, {
      headers: { Accept: 'application/vnd.github+json', 'User-Agent': `KillerMCP/${currentVersion}` },
      signal: AbortSignal.timeout(3000),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const release = await response.json();
    const latestVersion = String(release.tag_name || '').replace(/^v/i, '');
    if (!parts(latestVersion)) throw new Error('Invalid release version');
    const checkedAt = new Date().toISOString();
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, `${JSON.stringify({ latestVersion, checkedAt })}\n`, 'utf8');
    return makeStatus(currentVersion, latestVersion, checkedAt, false);
  }
  catch {
    return {
      installedVersion: currentVersion,
      latestVersion: null,
      updateAvailable: false,
      checkedAt: null,
      cached: false,
      installerUrl,
      releaseUrl,
      unavailable: true,
    };
  }
}

function result(status) {
  return { content: [{ type: 'text', text: JSON.stringify(status) }] };
}

export async function createUpdateAdapter(currentVersion) {
  const status = await checkForUpdate(currentVersion);
  return {
    status,
    tool: {
      name: 'killermcp_update_status',
      description: 'Check the installed KillerMCP version and whether a newer signed Windows installer is available. Use for requests such as "killer update status".',
      inputSchema: { type: 'object', properties: {}, additionalProperties: false },
    },
    call(input) {
      if (!input || typeof input !== 'object' || Array.isArray(input) || Object.keys(input).length) {
        return Promise.resolve({ content: [{ type: 'text', text: 'This tool takes no arguments' }], isError: true });
      }
      return Promise.resolve(result(status));
    },
  };
}

export function updateInstruction(status) {
  if (!status.updateAvailable) return '';
  return `KillerMCP ${status.latestVersion} is available. This computer has ${status.installedVersion}. The user can download the signed installer from ${status.installerUrl}. Do not download or install it without the user's approval.`;
}
