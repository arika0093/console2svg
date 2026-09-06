#!/usr/bin/env bash
set -euo pipefail

sudo apt-get install -y unzip tar

mkdir -p ./release-upload

for dir in ./native-artifacts/native-*/; do
  rid=$(basename "$dir" | sed 's/^native-//')
  bundle_dir="./bundle-${rid}"
  rm -rf "$bundle_dir"
  mkdir -p "$bundle_dir"

  if [[ "$rid" == win-* ]]; then
    cp "$dir/console2svg.exe" "$bundle_dir/console2svg.exe"
    for file in "$dir"/*.dll; do
      [ -f "$file" ] && cp "$file" "$bundle_dir/"
    done
    for file in "$dir"/*.exe; do
      [ -f "$file" ] || continue
      [ "$(basename "$file")" = "console2svg.exe" ] && continue
      cp "$file" "$bundle_dir/"
    done
  else
    cp "$dir/console2svg" "$bundle_dir/console2svg"
    chmod +x "$bundle_dir/console2svg"
    for file in "$dir"/*.so "$dir"/*.dylib; do
      [ -f "$file" ] && cp "$file" "$bundle_dir/"
    done
  fi

  case "$rid" in
    win-x64) ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip" ;;
    win-arm64) ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-winarm64-lgpl-shared.zip" ;;
    *) ffmpeg_zip_url="" ;;
  esac

  if [ -n "$ffmpeg_zip_url" ]; then
    ffmpeg_zip="./ffmpeg-${rid}.zip"
    curl -fsSL -o "$ffmpeg_zip" "$ffmpeg_zip_url"
    if ! unzip -t "$ffmpeg_zip" >/dev/null; then
      echo "Downloaded ffmpeg archive is corrupt: $ffmpeg_zip" >&2
      exit 1
    fi
    sha256sum "$ffmpeg_zip"

    ffmpeg_extract="./ffmpeg-extract-${rid}"
    mkdir -p "$ffmpeg_extract"
    unzip -q "$ffmpeg_zip" -d "$ffmpeg_extract"
    top_dir=$(find "$ffmpeg_extract" -mindepth 1 -maxdepth 1 -type d -print -quit)
    ffmpeg_bin_dir="${top_dir}/bin"
    if [ ! -f "$ffmpeg_bin_dir/ffmpeg.exe" ]; then
      echo "ffmpeg.exe not found at $ffmpeg_bin_dir/ffmpeg.exe." >&2
      exit 1
    fi
    mkdir -p "$bundle_dir/ffmpeg"
    cp "$ffmpeg_bin_dir"/*.exe "$bundle_dir/ffmpeg/"
    cp "$ffmpeg_bin_dir"/*.dll "$bundle_dir/ffmpeg/"
    cp "${top_dir}/LICENSE.txt" "$bundle_dir/ffmpeg/ffmpeg-LICENSE.txt"
  fi

  case "$rid" in
    win-*)
      archive_name="./release-upload/console2svg-${rid}.zip"
      (cd "$bundle_dir" && zip -q -r "../${archive_name#./}" .)
      ;;
    *)
      archive_name="./release-upload/console2svg-${rid}.tar.gz"
      tar -czf "$archive_name" -C "$bundle_dir" .
      ;;
  esac
  echo "Created $archive_name"
done
