#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

work_dir="$(mktemp -d)"
socket="console2svg-interactive-$$"
session="interactive"

cleanup() {
    tmux -L "$socket" kill-server 2>/dev/null || true
    rm -rf "$work_dir"
}
trap cleanup EXIT

TMUX="" tmux -L "$socket" -f /dev/null new-session \
    -d -x 100 -y 20 -s "$session" -c "$work_dir" \
    "env PS1='$ ' TERM=xterm-256color '$CONSOLE2SVG_BIN' \
        capture -w 100 -h 20 -v -d macos --sleep 0.5 \
        --verbose '$DEMO_ROOT/logs/cmd-interactive.log' \
        -o '$work_dir/cmd-interactive.svg' \
        -- '$CONSOLE2SVG_BIN' interactive -w 100 -h 20 \
        -o '$work_dir/interactive-inner.svg' \
        -- bash --noprofile --norc"

wait_for_pane "$socket" "$session:0.0" "F9: Record start"
sleep 0.5
send_key_until_pane "$socket" "$session:0.0" F9 "Started"
wait_for_pane "$socket" "$session:0.0" "REC (F9:End"
sleep 0.5

type_slowly "$socket" "$session:0.0" 'echo "interactive capture"'
tmux -L "$socket" send-keys -t "$session:0.0" Enter
wait_for_pane "$socket" "$session:0.0" "interactive capture"

tmux -L "$socket" send-keys -t "$session:0.0" F9

inner_capture=""
for _ in $(seq 1 100); do
    inner_capture="$(
        find "$work_dir" -maxdepth 1 -name 'interactive-inner_*.svg' -print -quit
    )"
    if [[ -n "$inner_capture" && -s "$inner_capture" ]]; then
        break
    fi
    sleep 0.1
done
if [[ -z "$inner_capture" || ! -s "$inner_capture" ]]; then
    printf 'Timed out waiting for the interactive recording\n' >&2
    exit 1
fi

sleep 1
tmux -L "$socket" send-keys -t "$session:0.0" C-d
wait_for_file "$work_dir/cmd-interactive.svg"
mv "$work_dir/cmd-interactive.svg" "$DEMO_ROOT/assets/cmd-interactive.svg"
