import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const siteDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(siteDir, '..');

const docsSrcDir = path.join(repoRoot, 'docs');
const publicDir = path.join(siteDir, 'public');

function copyRecursiveSync(src, dest) {
  if (!fs.existsSync(src)) return;
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const srcPath = path.join(src, entry.name);
    const destPath = path.join(dest, entry.name);
    if (entry.isDirectory()) {
      copyRecursiveSync(srcPath, destPath);
    } else {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

export function syncDocs() {
  // Seed the single public asset tree for local development. CI generates into
  // this directory directly before Astro builds the site.
  const assetsSrc = path.join(docsSrcDir, 'assets');
  if (fs.existsSync(assetsSrc)) {
    const publicAssets = path.join(publicDir, 'assets');
    const publicDocsAssets = path.join(publicDir, 'docs', 'assets');
    fs.rmSync(publicAssets, { recursive: true, force: true });
    fs.rmSync(publicDocsAssets, { recursive: true, force: true });
    copyRecursiveSync(assetsSrc, publicAssets);
    console.log(`[sync-docs] Synchronized docs/assets -> public/assets`);
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) syncDocs();
