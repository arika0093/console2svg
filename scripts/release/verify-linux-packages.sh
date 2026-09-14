#!/usr/bin/env bash
set -euo pipefail

shopt -s nullglob

debs=(./release-upload/*.deb)
rpms=(./release-upload/*.rpm)

if [ "${#debs[@]}" -eq 0 ] || [ "${#rpms[@]}" -eq 0 ]; then
  echo "Expected both .deb and .rpm packages in ./release-upload." >&2
  exit 1
fi

for deb in "${debs[@]}"; do
  if dpkg-deb -c "$deb" | grep -qE '[[:space:]]\./usr/local/'; then
    echo "Debian package must not install files under /usr/local: $deb" >&2
    exit 1
  fi

  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  dpkg-deb -x "$deb" "$tmp"

  test -x "$tmp/usr/lib/console2svg/console2svg"
  test -f "$tmp/usr/lib/console2svg/libconsole2svg_resvg.so"
  test -L "$tmp/usr/bin/console2svg"
  test "$(readlink "$tmp/usr/bin/console2svg")" = "../lib/console2svg/console2svg"

  rm -rf "$tmp"
  trap - EXIT
done

for rpm_package in "${rpms[@]}"; do
  files=$(rpm -qlp "$rpm_package")
  if grep -q '^/usr/local/' <<<"$files"; then
    echo "RPM package must not install files under /usr/local: $rpm_package" >&2
    exit 1
  fi

  grep -qx '/usr/bin/console2svg' <<<"$files"
  grep -qx '/usr/lib/console2svg/console2svg' <<<"$files"
  grep -qx '/usr/lib/console2svg/libconsole2svg_resvg.so' <<<"$files"
done
