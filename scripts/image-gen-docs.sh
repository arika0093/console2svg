#!/usr/bin/env bash
# Generate documentation-site images under docs-site/public/assets.
# These files are build artifacts and are intentionally not committed.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/image-gen-common.sh"

assets_dir="$repo_root/docs-site/public/assets"
mkdir -p "$assets_dir"

# Static images shared with README are owned by ./assets and copied only for the docs build.
cp "$repo_root/assets/cmd-bash-vim.svg" "$assets_dir/cmd-bash-vim.svg"
cp "$repo_root/assets/cmd-interactive.svg" "$assets_dir/cmd-interactive.svg"

# Documentation images are declared next to their Markdown examples.
console2svg batch markdown \
  -i "$repo_root" \
  -o "$assets_dir" \
  --filter "docs/**" \
  --link-base /assets
