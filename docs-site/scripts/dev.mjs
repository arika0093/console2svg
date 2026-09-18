import chokidar from 'chokidar';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { syncDocs } from './sync-docs.mjs';

const siteDir = fileURLToPath(new URL('..', import.meta.url));
const repoRoot = fileURLToPath(new URL('../..', import.meta.url));
const watchedPaths = [`${repoRoot}/docs/assets`];
let syncTimer;

syncDocs();

const watcher = chokidar.watch(watchedPaths, {
  ignoreInitial: true,
  usePolling: true,
  interval: 200,
});

const scheduleSync = () => {
  clearTimeout(syncTimer);
  syncTimer = setTimeout(() => {
    try {
      syncDocs();
    } catch (error) {
      console.error('[sync-docs] Failed to synchronize changed documentation:', error);
    }
  }, 100);
};

watcher.on('add', scheduleSync).on('change', scheduleSync).on('unlink', scheduleSync);

const astro = spawn('astro', ['dev', ...process.argv.slice(2)], {
  cwd: siteDir,
  stdio: 'inherit',
  shell: process.platform === 'win32',
});

const shutdown = async (signal) => {
  await watcher.close();
  if (!astro.killed) astro.kill(signal);
};

process.on('SIGINT', () => void shutdown('SIGINT'));
process.on('SIGTERM', () => void shutdown('SIGTERM'));
astro.on('exit', (code, signal) => {
  void shutdown(signal ?? 'SIGTERM').then(() => process.exit(code ?? 1));
});
