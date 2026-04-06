#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');
const https = require('https');
const { spawnSync } = require('child_process');
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
const distDir = path.join(__dirname, '..', 'dist');
const destPath = path.join(distDir, `console2svg${isWin ? '.exe' : ''}`);

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

function download(downloadUrl, redirects, onFinish) {
  if (redirects > 5) {
    fail('console2svg: too many redirects while downloading.');
  }

  const proxy = getProxyForUrl(downloadUrl);
  const agent = proxy ? new HttpsProxyAgent(proxy) : undefined;
  const urlObj = new URL(downloadUrl);

  const tempPath = `${destPath}.tmp`;

  const request = https.get(
    {
      hostname: urlObj.hostname,
      path: urlObj.pathname + urlObj.search,
      protocol: urlObj.protocol,
      port: urlObj.port,
      agent,
      headers: {
        'User-Agent': 'console2svg-npm-wrapper',
        Accept: 'application/octet-stream'
      }
    },
    (res) => {
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        res.resume();
        download(res.headers.location, redirects + 1, onFinish);
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
            onFinish(tempPath);
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

if (isWin) {
  // On Windows: download the ffmpeg bundle zip (console2svg.exe + ffmpeg.exe + DLLs)
  const zipFileName = `console2svg-${rid}-ffmpeg.zip`;
  const zipUrl = `https://github.com/arika0093/console2svg/releases/download/v${version}/${zipFileName}`;

  download(zipUrl, 0, (tempZipPath) => {
    // Extract the zip into distDir using PowerShell
    const result = spawnSync(
      'powershell',
      [
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        `Expand-Archive -LiteralPath '${tempZipPath}' -DestinationPath '${distDir}' -Force`
      ],
      { stdio: 'inherit' }
    );

    try { fs.unlinkSync(tempZipPath); } catch { /* ignore */ }

    if (result.status !== 0) {
      fail('console2svg: failed to extract zip bundle.');
    }

    if (!fs.existsSync(destPath)) {
      fail('console2svg: console2svg.exe not found after extraction.');
    }

    process.exit(0);
  });
} else {
  // On Linux/macOS: download the single binary
  const fileName = `console2svg-${rid}`;
  const url = `https://github.com/arika0093/console2svg/releases/download/v${version}/${fileName}`;

  download(url, 0, (tempPath) => {
    fs.renameSync(tempPath, destPath);
    fs.chmodSync(destPath, 0o755);
    process.exit(0);
  });
}

