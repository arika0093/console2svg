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

if (arch !== 'x64') {
  console.error(`console2svg: unsupported architecture: ${arch}`);
  console.error('Use the .NET tool or build from source for this platform.');
  process.exit(1);
}

let rid;
if (platform === 'win32') {
  rid = 'win-x64';
} else if (platform === 'linux') {
  rid = 'linux-x64';
} else if (platform === 'darwin') {
  rid = 'osx-x64';
} else {
  console.error(`console2svg: unsupported platform: ${platform}`);
  console.error('Use the .NET tool or build from source for this platform.');
  process.exit(1);
}

const isWin = platform === 'win32';
const ext = isWin ? '.exe' : '';
const fileName = `console2svg-${rid}${ext}`;
const url = `https://github.com/arika0093/console2svg/releases/download/v${version}/${fileName}`;

const distDir = path.join(__dirname, '..', 'dist');
const destPath = path.join(distDir, `console2svg${ext}`);
const tempPath = `${destPath}.tmp`;

if (fs.existsSync(destPath)) {
  process.exit(0);
}

fs.mkdirSync(distDir, { recursive: true });

function fail(message, err) {
  if (err) {
    console.error(message, err.message || err);
  } else {
    console.error(message);
  }
  process.exit(1);
}

function download(downloadUrl, redirects) {
  if (redirects > 5) {
    fail('console2svg: too many redirects while downloading.');
  }

  const proxy = getProxyForUrl(downloadUrl);
  const agent = proxy ? new HttpsProxyAgent(proxy) : undefined;
  const url = new URL(downloadUrl);

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
        download(res.headers.location, redirects + 1);
        return;
      }

      if (res.statusCode !== 200) {
        res.resume();
        fail(`console2svg: download failed (${res.statusCode}) from ${downloadUrl}`);
      }

      const file = fs.createWriteStream(tempPath);
      res.pipe(file);
      file.on('finish', () => {
        file.close(() => {
          try {
            fs.renameSync(tempPath, destPath);
            if (!isWin) {
              fs.chmodSync(destPath, 0o755);
            }
            process.exit(0);
          } catch (err) {
            fail('console2svg: failed to finalize download.', err);
          }
        });
      });
      file.on('error', (err) => {
        try {
          fs.unlinkSync(tempPath);
        } catch {
          // ignore
        }
        fail('console2svg: write failed.', err);
      });
    }
  );

  request.on('error', (err) => {
    fail('console2svg: request failed.', err);
  });
}

download(url, 0);
