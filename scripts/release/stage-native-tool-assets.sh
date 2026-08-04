#!/usr/bin/env bash
set -euo pipefail

tool_assets_dir="./tool-native-assets"
rm -rf "$tool_assets_dir"

for rid in linux-x64 linux-arm64 osx-x64 osx-arm64; do
  src="./native-artifacts/native-${rid}"
  dest="${tool_assets_dir}/${rid}"
  test -f "${src}/console2svg"
  mkdir -p "$dest"
  cp "${src}/console2svg" "$dest/"
  find "$src" -maxdepth 1 -type f \( -name '*.so' -o -name '*.dylib' \) -exec cp {} "$dest/" \;
done

for rid in win-x64 win-arm64; do
  src="./native-artifacts/native-${rid}"
  dest="${tool_assets_dir}/${rid}"
  test -f "${src}/console2svg.exe"
  mkdir -p "$dest"
  find "$src" -maxdepth 1 -type f \( -name '*.exe' -o -name '*.dll' \) -exec cp {} "$dest/" \;

  case "$rid" in
    win-x64) ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip" ;;
    win-arm64) ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-winarm64-lgpl-shared.zip" ;;
  esac

  ffmpeg_extract="${tool_assets_dir}/.ffmpeg-${rid}"
  curl -fsSL -o "${ffmpeg_extract}.zip" "$ffmpeg_zip_url"
  unzip -q "${ffmpeg_extract}.zip" -d "$ffmpeg_extract"
  ffmpeg_bin_dir="$(find "$ffmpeg_extract" -type f -name ffmpeg.exe -printf '%h\n' | head -n 1)"
  test -n "$ffmpeg_bin_dir"
  mkdir -p "${dest}/ffmpeg"
  cp "${ffmpeg_bin_dir}"/*.exe "${dest}/ffmpeg/"
  cp "${ffmpeg_bin_dir}"/*.dll "${dest}/ffmpeg/"
  cp "$(dirname "$ffmpeg_bin_dir")/LICENSE.txt" "${dest}/ffmpeg/ffmpeg-LICENSE.txt"
  rm -rf "$ffmpeg_extract" "${ffmpeg_extract}.zip"
done
