#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

"$CONSOLE2SVG_BIN" \
    -o "$DEMO_ROOT/assets/cmd-loop.svg" \
    --verbose "$DEMO_ROOT/logs/cmd-loop.log" \
    -w 100 -h 20 -v -d --timeout 3 \
    -- copilot --banner
