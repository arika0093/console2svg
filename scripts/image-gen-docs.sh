#!/usr/bin/env bash
# Generate documentation-site images under docs-site/public/assets.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/image-gen-common.sh"

assets_dir="$repo_root/docs-site/public/assets"
mkdir -p "$assets_dir"

# Documentation images are declared next to their Markdown examples.
console2svg batch markdown \
  -i "$repo_root" \
  -o "$assets_dir" \
  --filter "docs/**" \
  --link-base /assets

# The interactive demo is not supported by batch markdown.
bash "$repo_root/scripts/demos/generate-interactive.sh"
