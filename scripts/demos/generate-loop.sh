#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

"$CONSOLE2SVG_BIN" \
    -o "$work_dir/cmd-loop.svg" \
    --verbose "$DEMO_ROOT/logs/cmd-loop.log" \
    -w 100 -h 24 -v -c -d macos-pc --timeout 5 --fps 30 \
    -- cmatrix -ab

# A command can time out successfully before emitting its first frame. Moving from
# a temporary path makes that case fail instead of silently keeping a stale asset.
mv "$work_dir/cmd-loop.svg" "$DEMO_ROOT/assets/cmd-loop.svg"
