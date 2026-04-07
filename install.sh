#!/usr/bin/env sh
set -eu

REPOSITORY="${REPOSITORY:-arika0093/console2svg}"
VERSION="${VERSION:-latest}"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "install.sh: required command not found: $1" >&2
    exit 1
  fi
}

normalize_arch() {
  case "$1" in
    x86_64|amd64)
      printf '%s' "x64"
      ;;
    aarch64|arm64)
      printf '%s' "arm64"
      ;;
    *)
      echo "install.sh: unsupported architecture: $1" >&2
      exit 1
      ;;
  esac
}

uname_s="$(uname -s)"
uname_m="$(uname -m)"
arch="$(normalize_arch "$uname_m")"

case "$uname_s" in
  Linux)
    rid="linux-${arch}"
    archive_ext=".tar.gz"
    ;;
  Darwin)
    rid="osx-${arch}"
    archive_ext=".tar.gz"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    rid="win-${arch}"
    archive_ext=".zip"
    ;;
  *)
    echo "install.sh: unsupported platform: $uname_s" >&2
    exit 1
    ;;
esac

if [ "${INSTALL_DIR:-}" = "" ]; then
  if [ "$archive_ext" = ".zip" ]; then
    INSTALL_DIR="${HOME}/bin"
  elif [ -w /usr/local/bin ]; then
    INSTALL_DIR="/usr/local/bin"
  else
    INSTALL_DIR="${HOME}/.local/bin"
  fi
fi

mkdir -p "$INSTALL_DIR"

case "$VERSION" in
  latest)
    release_base_url="https://github.com/${REPOSITORY}/releases/latest/download"
    ;;
  v*)
    release_base_url="https://github.com/${REPOSITORY}/releases/download/${VERSION}"
    ;;
  *)
    release_base_url="https://github.com/${REPOSITORY}/releases/download/v${VERSION}"
    ;;
esac

archive_name="console2svg-${rid}${archive_ext}"
archive_url="${release_base_url}/${archive_name}"

require_command curl
if [ "$archive_ext" = ".zip" ]; then
  require_command unzip
else
  require_command tar
fi

tmpdir="$(mktemp -d)"
cleanup() {
  rm -rf "$tmpdir"
}
trap cleanup EXIT INT TERM

archive_path="${tmpdir}/${archive_name}"

echo "Installing ${archive_name} from ${archive_url}"
curl -fsSL -o "$archive_path" "$archive_url"

if [ "$archive_ext" = ".zip" ]; then
  unzip -q -o "$archive_path" -d "$INSTALL_DIR"
else
  tar -xzf "$archive_path" -C "$INSTALL_DIR"
fi

binary_name="console2svg"
if [ "$archive_ext" = ".zip" ]; then
  binary_name="${binary_name}.exe"
fi

if [ ! -f "${INSTALL_DIR}/${binary_name}" ]; then
  echo "install.sh: expected ${binary_name} in ${archive_name}" >&2
  exit 1
fi

if [ "$archive_ext" != ".zip" ]; then
  chmod 755 "${INSTALL_DIR}/${binary_name}"
fi

echo "Installed ${binary_name} to ${INSTALL_DIR}"
case ":${PATH}:" in
  *:"${INSTALL_DIR}":*)
    ;;
  *)
    echo "Add ${INSTALL_DIR} to PATH if it is not already there." >&2
    ;;
esac
