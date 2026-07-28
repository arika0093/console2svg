#!/usr/bin/env bash
set -euo pipefail

# Download and install a console2svg release binary.
# Called from action.yml.
# Required env: VERSION, TOOL_CACHE, RUNNER_OS, RUNNER_ARCH

case "$RUNNER_OS/$RUNNER_ARCH" in
  Linux/X64)     RID="linux-x64"   ; EXT="" ;;
  Linux/ARM64)   RID="linux-arm64" ; EXT="" ;;
  Windows/X64)   RID="win-x64"     ; EXT=".exe" ;;
  Windows/ARM64) RID="win-arm64"   ; EXT=".exe" ;;
  macOS/X64)     RID="osx-x64"     ; EXT="" ;;
  macOS/ARM64)   RID="osx-arm64"   ; EXT="" ;;
  *) echo "Unsupported OS/arch: $RUNNER_OS/$RUNNER_ARCH" >&2; exit 1 ;;
esac

INSTALL_DIR="$TOOL_CACHE/console2svg/$VERSION/$RUNNER_ARCH"
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

# Try new format (v0.8+): archives (tar.gz/zip)
if [ -n "${EXT}" ]; then
  FILE_NEW="console2svg-${RID}.zip"
else
  FILE_NEW="console2svg-${RID}.tar.gz"
fi

if [ "$VERSION" = "latest" ]; then
  URL_NEW="https://github.com/arika0093/console2svg/releases/latest/download/${FILE_NEW}"
else
  URL_NEW="https://github.com/arika0093/console2svg/releases/download/v${VERSION}/${FILE_NEW}"
fi

echo "Trying new format (v0.8+): ${URL_NEW} ..."
if curl -fsSL -L -o "${INSTALL_DIR}/${FILE_NEW}" "$URL_NEW" 2>/dev/null; then
  echo "Downloaded new format"
  if [ -n "${EXT}" ]; then
    unzip -q "${INSTALL_DIR}/${FILE_NEW}" -d "$INSTALL_DIR"
  else
    tar -xzf "${INSTALL_DIR}/${FILE_NEW}" -C "$INSTALL_DIR"
  fi
  rm -f "${INSTALL_DIR}/${FILE_NEW}"
else
  # Fallback to old format (v0.7): raw binary
  if [ -n "${EXT}" ]; then
    FILE_OLD="console2svg-${RID}${EXT}"
  else
    FILE_OLD="console2svg-${RID}"
  fi

  if [ "$VERSION" = "latest" ]; then
    URL_OLD="https://github.com/arika0093/console2svg/releases/latest/download/${FILE_OLD}"
  else
    URL_OLD="https://github.com/arika0093/console2svg/releases/download/v${VERSION}/${FILE_OLD}"
  fi

  echo "Falling back to old format (v0.7): ${URL_OLD} ..."
  curl -fsSL -L -o "${INSTALL_DIR}/console2svg${EXT}" "$URL_OLD"
fi

[ -z "${EXT}" ] && chmod +x "${INSTALL_DIR}/console2svg"
echo "$INSTALL_DIR" >> "$GITHUB_PATH"
