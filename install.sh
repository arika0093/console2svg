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
    runtime_name="libresvg_wrapper.so"
    ;;
  Darwin)
    rid="osx-${arch}"
    archive_ext=".tar.gz"
    runtime_name="libresvg_wrapper.dylib"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    rid="win-${arch}"
    archive_ext=".zip"
    runtime_name="resvg_wrapper.dll"
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
extract_dir="${tmpdir}/extract"
mkdir -p "$extract_dir"

echo "Installing ${archive_name} from ${archive_url}"
curl -fsSL -o "$archive_path" "$archive_url"

if [ "$archive_ext" = ".zip" ]; then
  unzip -q "$archive_path" -d "$extract_dir"
else
  tar -xzf "$archive_path" -C "$extract_dir"
fi

binary_name="console2svg"
if [ "$archive_ext" = ".zip" ]; then
  binary_name="${binary_name}.exe"
fi

if [ ! -f "${extract_dir}/${binary_name}" ]; then
  echo "install.sh: expected ${binary_name} in ${archive_name}" >&2
  exit 1
fi

cp -f "${extract_dir}/${binary_name}" "${INSTALL_DIR}/${binary_name}"

if [ -f "${extract_dir}/${runtime_name}" ]; then
  cp -f "${extract_dir}/${runtime_name}" "${INSTALL_DIR}/${runtime_name}"
fi

if [ "$archive_ext" = ".zip" ]; then
  if [ -f "${extract_dir}/ffmpeg.exe" ]; then
    cp -f "${extract_dir}/ffmpeg.exe" "${INSTALL_DIR}/ffmpeg.exe"
  fi
  if [ -f "${extract_dir}/ffmpeg-LICENSE.txt" ]; then
    cp -f "${extract_dir}/ffmpeg-LICENSE.txt" "${INSTALL_DIR}/ffmpeg-LICENSE.txt"
  fi
else
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
