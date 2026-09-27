import { execFile } from 'node:child_process';

function parts(value) {
  return String(value).trim().split('.').map(part => Number.parseInt(part, 10) || 0);
}

function atLeast(actual, minimum) {
  const left = parts(actual);
  const right = parts(minimum);
  for (let index = 0; index < Math.max(left.length, right.length); index++) {
    const difference = (left[index] ?? 0) - (right[index] ?? 0);
    if (difference !== 0) return difference > 0;
  }
  return true;
}

export function supportsWindowsFileVersion(path, minimum) {
  if (process.platform !== 'win32' || !path.toLowerCase().endsWith('.exe')) {
    return Promise.resolve(true);
  }
  return new Promise(resolve => {
    execFile('powershell.exe', [
      '-NoProfile', '-NonInteractive', '-Command',
      '& { param($p) (Get-Item -LiteralPath $p).VersionInfo.FileVersion }', path,
    ], { encoding: 'utf8', windowsHide: true, timeout: 5000, maxBuffer: 4096 },
    (error, stdout) => resolve(!error && atLeast(stdout, minimum)));
  });
}
