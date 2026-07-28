#!/usr/bin/env bash
set -euo pipefail

# Build and install console2svg from source (develop branch).
# Called from action.yml.
# Required env: ACTION_PATH, TOOL_CACHE, RUNNER_OS, RUNNER_ARCH

case "$RUNNER_OS/$RUNNER_ARCH" in
  Linux/X64)     RID="linux-x64"   ; EXT="" ;;
  Linux/ARM64)   RID="linux-arm64" ; EXT="" ;;
  Windows/X64)   RID="win-x64"     ; EXT=".exe" ;;
  Windows/ARM64) RID="win-arm64"   ; EXT=".exe" ;;
  macOS/X64)     RID="osx-x64"     ; EXT="" ;;
  macOS/ARM64)   RID="osx-arm64"   ; EXT="" ;;
  *) echo "Unsupported OS/arch: $RUNNER_OS/$RUNNER_ARCH" >&2; exit 1 ;;
esac

case "$RID" in
  linux-x64)   RUST_TARGET="x86_64-unknown-linux-gnu" ;;
  linux-arm64) RUST_TARGET="aarch64-unknown-linux-gnu" ;;
  win-x64)     RUST_TARGET="x86_64-pc-windows-msvc" ;;
  win-arm64)   RUST_TARGET="aarch64-pc-windows-msvc" ;;
  osx-x64)     RUST_TARGET="x86_64-apple-darwin" ;;
  osx-arm64)   RUST_TARGET="aarch64-apple-darwin" ;;
esac
rustup target add "$RUST_TARGET"

INSTALL_DIR="$TOOL_CACHE/console2svg/develop/$RUNNER_ARCH"
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
publish_dir="$INSTALL_DIR/publish"

export NBGV_GitEngine=Disabled
dotnet publish "$ACTION_PATH/src/ConsoleToSvg/ConsoleToSvg.csproj" \
  -c Release \
  -r "$RID" \
  -p:PublishAot=true \
  -p:SelfContained=true \
  -p:BuildResvgNative=false \
  -o "$publish_dir"

"$ACTION_PATH/scripts/build-resvg-native.sh" "$RUST_TARGET" "$publish_dir"

if [ -f "$publish_dir/ConsoleToSvg${EXT}" ]; then
  cp "$publish_dir/ConsoleToSvg${EXT}" "$INSTALL_DIR/console2svg${EXT}"
elif [ -f "$publish_dir/console2svg${EXT}" ]; then
  cp "$publish_dir/console2svg${EXT}" "$INSTALL_DIR/console2svg${EXT}"
else
  echo "Published binary not found in $publish_dir" >&2
  ls -la "$publish_dir"
  exit 1
fi

# Native runtime libraries and child executables must be next to the executable.
if [ -n "${EXT}" ]; then
  for f in "$publish_dir"/*.dll; do
    [ -f "$f" ] && cp "$f" "$INSTALL_DIR/"
  done
  for f in "$publish_dir"/*.exe; do
    [ -f "$f" ] || continue
    [ "$(basename "$f")" = "console2svg${EXT}" ] && continue
    cp "$f" "$INSTALL_DIR/"
  done
else
  for f in "$publish_dir"/*.so "$publish_dir"/*.dylib; do
    [ -f "$f" ] && cp "$f" "$INSTALL_DIR/"
  done
fi

[ -z "${EXT}" ] && chmod +x "$INSTALL_DIR/console2svg"
rm -rf "$publish_dir"
echo "$INSTALL_DIR" >> "$GITHUB_PATH"
