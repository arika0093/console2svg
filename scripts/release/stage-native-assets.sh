#!/usr/bin/env bash
set -euo pipefail

for rid in linux-x64 linux-arm64; do
  src="./native-artifacts/native-${rid}/libconsole2svg_resvg.so"
  dest="./native-assets/${rid}/libconsole2svg_resvg.so"
  test -f "$src"
  mkdir -p "$(dirname "$dest")"
  cp "$src" "$dest"
done

for rid in win-x64 win-arm64; do
  src="./native-artifacts/native-${rid}/console2svg_resvg.dll"
  dest="./native-assets/${rid}/console2svg_resvg.dll"
  test -f "$src"
  mkdir -p "$(dirname "$dest")"
  cp "$src" "$dest"
done

for rid in osx-x64 osx-arm64; do
  src="./native-artifacts/native-${rid}/libconsole2svg_resvg.dylib"
  dest="./native-assets/${rid}/libconsole2svg_resvg.dylib"
  test -f "$src"
  mkdir -p "$(dirname "$dest")"
  cp "$src" "$dest"
done
