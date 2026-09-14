#!/usr/bin/env bash
set -euo pipefail

shopt -s nullglob

debs=(./release-upload/*.deb)
rpms=(./release-upload/*.rpm)

if [ "${#debs[@]}" -eq 0 ] || [ "${#rpms[@]}" -eq 0 ]; then
  echo "Expected both .deb and .rpm packages in ./release-upload." >&2
  exit 1
fi

smoke_test() {
  local executable=$1 output
  output=$(mktemp --suffix=.svg)
  "$executable" capture -o "$output" -- /usr/bin/printf hello
  grep 'hello' "$output" >/dev/null
  rm -f "$output"
}

for deb in "${debs[@]}"; do
  if dpkg-deb -c "$deb" | grep -E '[[:space:]]\./usr/local/' >/dev/null; then
    echo "Debian package must not install files under /usr/local: $deb" >&2
    exit 1
  fi

  test "$(dpkg-deb -f "$deb" Package)" = "console2svg"
  deb_arch=$(dpkg-deb -f "$deb" Architecture)

  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  dpkg-deb -x "$deb" "$tmp"

  test -x "$tmp/usr/lib/console2svg/console2svg"
  test -f "$tmp/usr/lib/console2svg/libconsole2svg_resvg.so"
  test -L "$tmp/usr/bin/console2svg"
  test "$(readlink "$tmp/usr/bin/console2svg")" = "../lib/console2svg/console2svg"

  if [ "$deb_arch" = "amd64" ] && [ "$(uname -m)" = "x86_64" ]; then
    smoke_test "$tmp/usr/bin/console2svg"
  fi

  rm -rf "$tmp"
  trap - EXIT
done

for rpm_package in "${rpms[@]}"; do
  files=$(rpm -qlp "$rpm_package")
  if grep '^/usr/local/' <<<"$files" >/dev/null; then
    echo "RPM package must not install files under /usr/local: $rpm_package" >&2
    exit 1
  fi

  grep -qx '/usr/bin/console2svg' <<<"$files"
  grep -qx '/usr/lib/console2svg/console2svg' <<<"$files"
  grep -qx '/usr/lib/console2svg/libconsole2svg_resvg.so' <<<"$files"

  rpm_arch=$(rpm -qp --queryformat '%{ARCH}' "$rpm_package")
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  (
    cd "$tmp"
    rpm2cpio "$OLDPWD/$rpm_package" | cpio -idm --quiet
  )

  test -x "$tmp/usr/lib/console2svg/console2svg"
  test -f "$tmp/usr/lib/console2svg/libconsole2svg_resvg.so"
  test -L "$tmp/usr/bin/console2svg"
  test "$(readlink "$tmp/usr/bin/console2svg")" = "../lib/console2svg/console2svg"

  if [ "$rpm_arch" = "x86_64" ] && [ "$(uname -m)" = "x86_64" ]; then
    smoke_test "$tmp/usr/bin/console2svg"
  fi

  rm -rf "$tmp"
  trap - EXIT
done
