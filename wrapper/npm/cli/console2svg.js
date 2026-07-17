#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const isWin = process.platform === 'win32';
const ext = isWin ? '.exe' : '';
const binPath = path.join(__dirname, '..', 'dist', `console2svg${ext}`);

if (!fs.existsSync(binPath)) {
  console.error('console2svg: binary not found.');
  console.error('Reinstall the package or use the .NET tool.');
  process.exit(1);
}

const child = spawn(binPath, process.argv.slice(2), { stdio: 'inherit' });
child.on('exit', (code) => {
  process.exit(code == null ? 1 : code);
});
