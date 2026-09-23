#!/usr/bin/env bash
# Generate README images directly under ./assets/.
# README links stay relative so they render on GitHub and in local checkouts.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/image-gen-common.sh"

readme_assets_dir="$repo_root/assets"
site_assets_dir="$repo_root/docs-site/public/assets"
mkdir -p "$readme_assets_dir/generated"

# README owns a separate manifest under ./assets/.
console2svg batch markdown -i "$repo_root" -o "$readme_assets_dir" --filter "README.md"

# Static snapshots shared with the docs site stay in sync with docs-site copies.
cp "$site_assets_dir/cmd-bash-vim.svg" "$readme_assets_dir/cmd-bash-vim.svg"
cp "$site_assets_dir/cmd-interactive.svg" "$readme_assets_dir/cmd-interactive.svg"
