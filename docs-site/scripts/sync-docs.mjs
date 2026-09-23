import { fileURLToPath } from 'node:url';

export function syncDocs() {
  // Documentation assets are generated at docs build time and are not committed.
  // Keep this hook for npm scripts.
}

if (process.argv[1] === fileURLToPath(import.meta.url)) syncDocs();
