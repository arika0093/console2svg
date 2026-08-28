#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

# Warm up the freshly installed Copilot CLI under the same PTY conditions. The
# rendered SVG is intentionally discarded; only its initialization side effects
# and process completion are needed here.
"$CONSOLE2SVG_BIN" \
    --stdout \
    -w 100 -h 20 -v -d --timeout 10 \
    -- copilot --banner >/dev/null

"$CONSOLE2SVG_BIN" \
    -o "$work_dir/cmd-loop.svg" \
    --verbose "$DEMO_ROOT/logs/cmd-loop.log" \
    -w 100 -h 20 -v -d --timeout 3 \
    -- copilot --banner

# A command can time out successfully before emitting its first frame. Moving from
# a temporary path makes that case fail instead of silently keeping a stale asset.
mv "$work_dir/cmd-loop.svg" "$DEMO_ROOT/assets/cmd-loop.svg"
