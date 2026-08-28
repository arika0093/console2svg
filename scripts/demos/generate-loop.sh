#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

# Warm up the freshly installed Copilot CLI in a disposable PTY. Keep an outer
# timeout here: Copilot can replace its child process during startup, which can
# leave console2svg waiting indefinitely for the replacement process to exit.
timeout --kill-after=2s 10s \
    script --quiet --command 'copilot --banner' /dev/null \
    >/dev/null 2>&1 || true

"$CONSOLE2SVG_BIN" \
    -o "$work_dir/cmd-loop.svg" \
    --verbose "$DEMO_ROOT/logs/cmd-loop.log" \
    -w 100 -h 20 -v -d --timeout 3 \
    -- copilot --banner

# A command can time out successfully before emitting its first frame. Moving from
# a temporary path makes that case fail instead of silently keeping a stale asset.
mv "$work_dir/cmd-loop.svg" "$DEMO_ROOT/assets/cmd-loop.svg"
