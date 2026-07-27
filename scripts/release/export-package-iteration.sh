#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <prerelease-version> <build-metadata>" >&2
  exit 64
fi

normalize() {
  printf '%s' "$1" |
    tr -cs '0-9A-Za-z.' '.' |
    sed -e 's/^\.*//' -e 's/\.*$//' -e 's/\.\+/\./g'
}

prerelease="$(normalize "$1")"
metadata="$(normalize "${2#+}")"
package_iteration=1

if [ -n "$prerelease" ]; then
  package_iteration="0.${prerelease}"
fi
if [ -n "$metadata" ]; then
  package_iteration="${package_iteration}.${metadata}"
fi

echo "package_iteration=$package_iteration" >> "$GITHUB_OUTPUT"
