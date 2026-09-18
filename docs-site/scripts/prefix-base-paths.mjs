import { readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = process.argv[2] ?? 'dist';
const rawBase = process.argv[3] ?? process.env.CONSOLE2SVG_DOCS_BASE ?? '';
const base = rawBase && rawBase !== '/'
  ? `/${rawBase.replace(/^\/+|\/+$/g, '')}`
  : '';

function prefixUrl(value) {
  if (
    !base ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value === base ||
    value.startsWith(`${base}/`)
  ) {
    return value;
  }
  return `${base}${value}`;
}

async function* files(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      yield* files(fullPath);
    } else {
      yield fullPath;
    }
  }
}

if (!base) process.exit(0);

for await (const file of files(root)) {
  const extension = path.extname(file).toLowerCase();
  if (extension !== '.html' && extension !== '.css') continue;

  const source = await readFile(file, 'utf8');
  let output = source;

  if (extension === '.html') {
    output = output.replace(
      /\b(href|src|poster|action)=(["'])(\/[^"'<>]*)\2/g,
      (match, name, quote, value) => `${name}=${quote}${prefixUrl(value)}${quote}`,
    );
  } else {
    output = output.replace(
      /url\((['"]?)(\/(?!\/)[^)'"\s]+)\1\)/g,
      (match, quote, value) => `url(${quote}${prefixUrl(value)}${quote})`,
    );
  }

  if (output !== source) await writeFile(file, output);
}
