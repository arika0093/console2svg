import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const siteDir = fileURLToPath(new URL('..', import.meta.url));

const astro = spawn('astro', ['dev', ...process.argv.slice(2)], {
  cwd: siteDir,
  stdio: 'inherit',
  shell: process.platform === 'win32',
});

const shutdown = async (signal) => {
  if (!astro.killed) astro.kill(signal);
};

process.on('SIGINT', () => void shutdown('SIGINT'));
process.on('SIGTERM', () => void shutdown('SIGTERM'));
astro.on('exit', (code, signal) => {
  void shutdown(signal ?? 'SIGTERM').then(() => process.exit(code ?? 1));
});
