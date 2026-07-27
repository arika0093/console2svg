#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <semver>" >&2
  exit 64
fi

semver="$1"
package_version="${semver%%[-+]*}"
package_iteration=1

if [[ "$semver" == *-* ]]; then
  prerelease="${semver#"$package_version"-}"
  prerelease="${prerelease%%+*}"
  prerelease="${prerelease//[^0-9A-Za-z.]/.}"
  prerelease="$(tr -s '.' <<< "$prerelease")"
  prerelease="${prerelease#.}"
  prerelease="${prerelease%.}"
  package_iteration="0.${prerelease}"
fi

if [[ "$semver" == *+* ]]; then
  metadata="${semver#*+}"
  metadata="${metadata//[^0-9A-Za-z.]/.}"
  metadata="$(tr -s '.' <<< "$metadata")"
  metadata="${metadata#.}"
  metadata="${metadata%.}"
  package_iteration="${package_iteration}.${metadata}"
fi

prerelease=false
if [[ "$semver" == *-* ]]; then
  prerelease=true
fi

{
  echo "semver=$semver"
  echo "package_version=$package_version"
  echo "package_iteration=$package_iteration"
  echo "prerelease=$prerelease"
} >> "$GITHUB_OUTPUT"
