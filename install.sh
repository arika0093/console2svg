#!/bin/bash
set -euo pipefail

# Install console2svg from GitHub releases.
# Usage:
#   curl -sSL https://raw.githubusercontent.com/arika0093/console2svg/main/install.sh | bash
#   CONSOLE2SVG_VERSION=0.8.0 bash install.sh
#   CONSOLE2SVG_INSTALL_DIR=/opt/console2svg bash install.sh

REPO="arika0093/console2svg"
VERSION="${CONSOLE2SVG_VERSION:-latest}"

OS=$(uname -s)
ARCH=$(uname -m)

case "$OS" in
  Linux)
    case "$ARCH" in
      x86_64)  RID="linux-x64" ;;
      aarch64) RID="linux-arm64" ;;
      *) echo "Unsupported Linux architecture: $ARCH" >&2; exit 1 ;;
    esac
    ;;
  Darwin)
    case "$ARCH" in
      x86_64) RID="osx-x64" ;;
      arm64)  RID="osx-arm64" ;;
      *) echo "Unsupported macOS architecture: $ARCH" >&2; exit 1 ;;
    esac
    ;;
  *)
    echo "Unsupported OS: $OS" >&2
    echo "For Windows, use npm or download the zip archive manually." >&2
    exit 1
    ;;
esac

# Choose install destination.
if [ -n "${CONSOLE2SVG_INSTALL_DIR:-}" ]; then
  INSTALL_DIR="$CONSOLE2SVG_INSTALL_DIR"
  BIN_DIR="${CONSOLE2SVG_BIN_DIR:-$(dirname "$INSTALL_DIR")/bin}"
elif [ -w /usr/local/lib ] && [ -w /usr/local/bin ]; then
  INSTALL_DIR="/usr/local/lib/console2svg"
  BIN_DIR="/usr/local/bin"
else
  INSTALL_DIR="${HOME}/.console2svg"
  BIN_DIR="${HOME}/.local/bin"
fi

echo "Installing console2svg (${RID}) ..."

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$INSTALL_DIR"

# Try new format (v0.8+): tar.gz archive
ARCHIVE_NEW="console2svg-${RID}.tar.gz"
if [ "$VERSION" = "latest" ]; then
  URL_NEW="https://github.com/${REPO}/releases/latest/download/${ARCHIVE_NEW}"
else
  URL_NEW="https://github.com/${REPO}/releases/download/v${VERSION}/${ARCHIVE_NEW}"
fi

if curl -fsSL -L -o "${TMP_DIR}/${ARCHIVE_NEW}" "$URL_NEW" 2>/dev/null; then
  tar -xzf "${TMP_DIR}/${ARCHIVE_NEW}" -C "$INSTALL_DIR"
else
  # Fallback to old format (v0.7): raw binary
  BINARY_OLD="console2svg-${RID}"
  if [ "$VERSION" = "latest" ]; then
    URL_OLD="https://github.com/${REPO}/releases/latest/download/${BINARY_OLD}"
  else
    URL_OLD="https://github.com/${REPO}/releases/download/v${VERSION}/${BINARY_OLD}"
  fi
  curl -fsSL -L -o "${INSTALL_DIR}/console2svg" "$URL_OLD"
fi

chmod +x "$INSTALL_DIR/console2svg"

mkdir -p "$BIN_DIR"
ln -sf "$INSTALL_DIR/console2svg" "$BIN_DIR/console2svg"

echo "console2svg installed to $BIN_DIR/console2svg"
if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
  echo "Add $BIN_DIR to your PATH to use it."
fi
