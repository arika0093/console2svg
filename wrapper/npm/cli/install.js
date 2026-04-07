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
const nativeLibPath = isWin
  ? null
  : path.join(distDir, platform === 'linux' ? 'libresvg_wrapper.so' : 'libresvg_wrapper.dylib');

if (fs.existsSync(destPath) && (isWin || fs.existsSync(nativeLibPath))) {
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

function download(downloadUrl, tempPath, redirects, onFinish, onFailure) {
  if (redirects > 5) {
    const err = new Error('console2svg: too many redirects while downloading.');
    if (onFailure) {
      onFailure(err);
      return;
    }
    fail(err.message);
  }

  const proxy = getProxyForUrl(downloadUrl);
  const agent = proxy ? new HttpsProxyAgent(proxy) : undefined;
  const urlObj = new URL(downloadUrl);

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
        download(res.headers.location, tempPath, redirects + 1, onFinish, onFailure);
        return;
      }

      if (res.statusCode !== 200) {
        res.resume();
        const err = new Error(`console2svg: download failed (${res.statusCode}) from ${downloadUrl}`);
        if (onFailure) {
          onFailure(err);
          return;
        }
        fail(err.message);
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
    if (onFailure) {
      onFailure(err);
      return;
    }
    fail('console2svg: request failed.', err);
  });
}

const releaseBaseUrl = `https://github.com/arika0093/console2svg/releases/download/v${version}`;

function extractZip(tempZipPath) {
  const psEscape = (p) => p.replace(/'/g, "''");
  const result = spawnSync(
    'powershell',
    [
      '-NoProfile',
      '-NonInteractive',
      '-Command',
      `Expand-Archive -LiteralPath '${psEscape(tempZipPath)}' -DestinationPath '${psEscape(distDir)}' -Force`
    ],
    { stdio: 'inherit' }
  );

  try { fs.unlinkSync(tempZipPath); } catch { /* ignore */ }

  if (result.status !== 0) {
    fail('console2svg: failed to extract zip bundle.');
  }
}

function extractTarGz(tempArchivePath) {
  const result = spawnSync(
    'tar',
    ['-xzf', tempArchivePath, '-C', distDir],
    { stdio: 'inherit' }
  );

  try { fs.unlinkSync(tempArchivePath); } catch { /* ignore */ }

  if (result.status !== 0) {
    fail('console2svg: failed to extract tar.gz bundle.');
  }
}

function finishBundleInstall() {
  if (!fs.existsSync(destPath)) {
    fail(`console2svg: ${path.basename(destPath)} not found after extraction.`);
  }

  if (!isWin) {
    fs.chmodSync(destPath, 0o755);
  }

  process.exit(0);
}

function downloadBundle(bundleNames, extractArchive, onAllMissing) {
  const tryNext = (index) => {
    if (index >= bundleNames.length) {
      onAllMissing();
      return;
    }

    const bundleName = bundleNames[index];
    const bundleUrl = `${releaseBaseUrl}/${bundleName}`;
    const tempPath = path.join(distDir, `${bundleName}.tmp`);

    download(
      bundleUrl,
      tempPath,
      0,
      (downloadedPath) => {
        extractArchive(downloadedPath);
        finishBundleInstall();
      },
      () => {
        try { fs.unlinkSync(tempPath); } catch { /* ignore */ }
        tryNext(index + 1);
      }
    );
  };

  tryNext(0);
}

function downloadLegacyAssets() {
  const fileName = `console2svg-${rid}${isWin ? '.exe' : ''}`;
  const url = `${releaseBaseUrl}/${fileName}`;

  download(url, `${destPath}.tmp`, 0, (tempPath) => {
    fs.renameSync(tempPath, destPath);
    if (!isWin) {
      fs.chmodSync(destPath, 0o755);
    }

    if (isWin) {
      const dllName = `resvg_wrapper-${rid}.dll`;
      download(
        `${releaseBaseUrl}/${dllName}`,
        path.join(distDir, `${dllName}.tmp`),
        0,
        (tempDllPath) => {
          fs.renameSync(tempDllPath, path.join(distDir, 'resvg_wrapper.dll'));
          process.exit(0);
        },
        () => process.exit(0)
      );
      return;
    }

    const nativeLibFileName =
      platform === 'linux'
        ? `libresvg_wrapper-${rid}.so`
        : `libresvg_wrapper-${rid}.dylib`;
    download(
      `${releaseBaseUrl}/${nativeLibFileName}`,
      `${nativeLibPath}.tmp`,
      0,
      (tempLibPath) => {
        fs.renameSync(tempLibPath, nativeLibPath);
        process.exit(0);
      }
    );
  });
}

if (isWin) {
  downloadBundle([`console2svg-${rid}.zip`], extractZip, downloadLegacyAssets);
} else {
  downloadBundle([`console2svg-${rid}.tar.gz`], extractTarGz, downloadLegacyAssets);
}
