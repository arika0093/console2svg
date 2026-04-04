#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: $0 <rid> <dest-dir> [version]"
  exit 1
fi

RID="$1"
DEST_DIR="$2"
VERSION="${3:-latest}"

mkdir -p "$DEST_DIR"

ASSET=""
BIN_NAME="resvg"

case "$RID" in
  linux-x64) ASSET="resvg-linux-x86_64.tar.gz" ;;
  osx-x64) ASSET="resvg-macos-x86_64.zip" ;;
  osx-arm64) ASSET="resvg-macos-aarch64.zip" ;;
  win-x64) ASSET="resvg-win64.zip"; BIN_NAME="resvg.exe" ;;
  *)
    echo "resvg sidecar is not available for $RID; skipping."
    exit 0
    ;;
esac

if [[ "$VERSION" == "latest" ]]; then
  URL="https://github.com/linebender/resvg/releases/latest/download/${ASSET}"
else
  URL="https://github.com/linebender/resvg/releases/download/v${VERSION}/${ASSET}"
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

ARCHIVE_PATH="$TMP_DIR/$ASSET"
curl -fsSL -L -o "$ARCHIVE_PATH" "$URL"

if [[ "$ASSET" == *.zip ]]; then
  unzip -q "$ARCHIVE_PATH" -d "$TMP_DIR/extract"
else
  tar -xzf "$ARCHIVE_PATH" -C "$TMP_DIR/extract"
fi

SOURCE_PATH="$(find "$TMP_DIR/extract" -type f \( -name "resvg" -o -name "resvg.exe" \) | head -n 1)"
if [[ -z "$SOURCE_PATH" ]]; then
  echo "resvg binary not found in archive: $ASSET"
  exit 1
fi

cp "$SOURCE_PATH" "$DEST_DIR/$BIN_NAME"

if [[ "$BIN_NAME" != *.exe ]]; then
  chmod +x "$DEST_DIR/$BIN_NAME"
fi
