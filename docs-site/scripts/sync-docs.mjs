import { fileURLToPath } from 'node:url';
import { copyFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

export async function syncDocs() {
  const source = fileURLToPath(new URL('../../install.sh', import.meta.url));
  const destination = fileURLToPath(new URL('../public/install.sh', import.meta.url));
  await mkdir(path.dirname(destination), { recursive: true });
  await copyFile(source, destination);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) await syncDocs();
