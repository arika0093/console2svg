#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <package-version> <package-iteration>" >&2
  exit 64
fi

package_version="$1"
package_iteration="$2"

for rid in linux-x64 linux-arm64; do
  case "$rid" in
    linux-x64)
      asset_arch=x64
      deb_arch=amd64
      rpm_arch=x86_64
      ;;
    linux-arm64)
      asset_arch=arm64
      deb_arch=arm64
      rpm_arch=aarch64
      ;;
  esac

  src="./native-artifacts/native-${rid}/console2svg"
  if [ ! -f "$src" ]; then
    continue
  fi
  chmod 755 "$src"

  so_src="./native-artifacts/native-${rid}/libconsole2svg_resvg.so"
  if [ ! -f "$so_src" ]; then
    echo "Native library not found: $so_src" >&2
    exit 1
  fi

  common_args=(
    -s dir
    -n console2svg
    -v "$package_version"
    --iteration "$package_iteration"
    --license Apache-2.0
    --url "https://github.com/${GITHUB_REPOSITORY}"
    --description "Convert terminal output to SVG images."
  )

  fpm "${common_args[@]}" -t deb -a "$deb_arch" -p "./release-upload/console2svg.${asset_arch}.deb" \
    "$src=/usr/local/bin/console2svg" \
    "$so_src=/usr/local/lib/console2svg/libconsole2svg_resvg.so"
  fpm "${common_args[@]}" -t rpm -a "$rpm_arch" -p "./release-upload/console2svg.${asset_arch}.rpm" \
    "$src=/usr/local/bin/console2svg" \
    "$so_src=/usr/local/lib/console2svg/libconsole2svg_resvg.so"
done
