#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');
const https = require('https');
const { HttpsProxyAgent } = require('https-proxy-agent');
const { getProxyForUrl } = require('proxy-from-env');

const pkg = require(path.join(__dirname, '..', 'package.json'));
const version = pkg.version;

const platform = process.platform;
const arch = process.arch;

const archSuffix =
  arch === 'x64' ? 'x64' :
  arch === 'arm64' ? 'arm64' :
  null;

if (!archSuffix) {
  console.error(`console2svg: unsupported architecture: ${arch}`);
  console.error('Use the .NET tool or build from source for this platform.');
  process.exit(1);
}

let rid;
if (platform === 'win32') {
  rid = `win-${archSuffix}`;
} else if (platform === 'linux') {
  rid = `linux-${archSuffix}`;
} else if (platform === 'darwin') {
  rid = `osx-${archSuffix}`;
} else {
  console.error(`console2svg: unsupported platform: ${platform}`);
  console.error('Use the .NET tool or build from source for this platform.');
  process.exit(1);
}

const isWin = platform === 'win32';
const ext = isWin ? '.exe' : '';

const distDir = path.join(__dirname, '..', 'dist');
fs.mkdirSync(distDir, { recursive: true });

const downloads = [
  {
    fileName: `console2svg-${rid}${ext}`,
    destPath: path.join(distDir, `console2svg${ext}`)
  }
];

const resvgFileName = (() => {
  if (rid === 'linux-x64' || rid === 'osx-x64' || rid === 'osx-arm64') {
    return `resvg-${rid}`;
  }

  if (rid === 'win-x64') {
    return 'resvg-win-x64.exe';
  }

  return null;
})();

if (resvgFileName) {
  downloads.push({
    fileName: resvgFileName,
    destPath: path.join(distDir, `resvg${isWin ? '.exe' : ''}`),
    optional: true
  });
}

function fail(message, err) {
  if (err) {
    console.error(message, err.message || err);
  } else {
    console.error(message);
  }
  process.exit(1);
}

function download(downloadUrl, destPath, redirects) {
  return new Promise((resolve, reject) => {
    if (redirects > 5) {
      reject(new Error('console2svg: too many redirects while downloading.'));
      return;
    }

    const proxy = getProxyForUrl(downloadUrl);
    const agent = proxy ? new HttpsProxyAgent(proxy) : undefined;
    const url = new URL(downloadUrl);
    const tempPath = `${destPath}.tmp`;

    const request = https.get(
      {
        hostname: url.hostname,
        path: url.pathname + url.search,
        protocol: url.protocol,
        port: url.port,
        agent,
        headers: {
          'User-Agent': 'console2svg-npm-wrapper',
          Accept: 'application/octet-stream'
        }
      },
      (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          res.resume();
          download(res.headers.location, destPath, redirects + 1).then(resolve, reject);
          return;
        }

        if (res.statusCode !== 200) {
          res.resume();
          reject(new Error(`console2svg: download failed (${res.statusCode}) from ${downloadUrl}`));
          return;
        }

        const file = fs.createWriteStream(tempPath);
        res.pipe(file);
        file.on('finish', () => {
          file.close(() => {
            try {
              fs.renameSync(tempPath, destPath);
              if (!destPath.endsWith('.exe')) {
                fs.chmodSync(destPath, 0o755);
              }
              resolve();
            } catch (err) {
              reject(err);
            }
          });
        });
        file.on('error', (err) => {
          try {
            fs.unlinkSync(tempPath);
          } catch {
            // ignore
          }
          reject(err);
        });
      }
    );

    request.on('error', reject);
  });
}

(async () => {
  for (const item of downloads) {
    if (fs.existsSync(item.destPath)) {
      continue;
    }

    const url = `https://github.com/arika0093/console2svg/releases/download/v${version}/${item.fileName}`;
    try {
      await download(url, item.destPath, 0);
    } catch (err) {
      if (item.optional) {
        console.warn(`console2svg: optional sidecar download skipped: ${item.fileName}`);
        continue;
      }

      fail('console2svg: request failed.', err);
    }
  }
})().then(
  () => process.exit(0),
  (err) => fail('console2svg: installation failed.', err)
);
