import { existsSync, statSync } from 'node:fs';
import { isAbsolute, join } from 'node:path';

const apps = {
  killerbench: { variable: 'KILLERBENCH_CLI', directory: 'KillerBench', executable: 'killerbench-cli.exe' },
  killerpdf: { variable: 'KILLERPDF_CLI' },
  killerscan: { variable: 'KILLERSCAN_CLI' },
  killershell: { variable: 'KILLERSHELL_CLI', directory: 'KillerShell', executable: 'KillerShell.Cli.exe' },
};

function isFile(path) {
  try { return isAbsolute(path) && existsSync(path) && statSync(path).isFile(); }
  catch { return false; }
}

export function discoverAppCli(app, environment = process.env) {
  const definition = apps[app];
  if (!definition) throw new Error(`Unknown app: ${app}`);
  const configured = environment[definition.variable];
  if (configured !== undefined) return isFile(configured) ? configured : null;
  if (!definition.executable) return null;

  const candidates = [];
  if (environment.LOCALAPPDATA) {
    candidates.push(join(environment.LOCALAPPDATA, 'Programs', definition.directory, definition.executable));
  }
  if (environment.ProgramFiles) {
    candidates.push(join(environment.ProgramFiles, definition.directory, definition.executable));
  }
  return candidates.find(isFile) ?? null;
}
