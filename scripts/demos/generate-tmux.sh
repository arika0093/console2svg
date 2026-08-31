#!/usr/bin/env bash

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

work_dir="$(mktemp -d)"
socket="console2svg-demo-$$"
session="demo"
recorder_pid=""

cleanup() {
    if [[ -n "$recorder_pid" ]] && kill -0 "$recorder_pid" 2>/dev/null; then
        kill "$recorder_pid"
    fi
    tmux -L "$socket" kill-server 2>/dev/null || true
    rm -rf "$work_dir"
}
trap cleanup EXIT

console2svg_path="$(command -v "$CONSOLE2SVG_BIN")"
mkdir -p "$work_dir/bin"
ln -s "$console2svg_path" "$work_dir/bin/console2svg"

TMUX="" tmux -L "$socket" -f /dev/null new-session \
    -d -x 80 -y 14 -s "$session" -c "$work_dir" \
    "env PATH='$work_dir/bin:$PATH' PS1='$ ' TERM=xterm-256color \
        bash --noprofile --norc"
tmux -L "$socket" set-option -t "$session" status-left "[demo] "
tmux -L "$socket" set-option -t "$session" status-right ""

wait_for_pane "$socket" "$session:0.0" "$"

"$CONSOLE2SVG_BIN" \
    capture -o "$work_dir/cmd-tmux-replay.svg" \
    --verbose "$DEMO_ROOT/logs/cmd-tmux-replay.log" \
    -w 80 -h 14 -v --sleep 0.5 \
    -- tmux -L "$socket" attach-session -t "$session" &
recorder_pid=$!

wait_for_tmux_client "$socket"
sleep 0.5

type_slowly "$socket" "$session:0.0" 'echo "hello"'
tmux -L "$socket" send-keys -t "$session:0.0" Enter
wait_for_pane "$socket" "$session:0.0" "hello"
sleep 0.5

type_slowly "$socket" "$session:0.0" 'echo "goodbye"'
tmux -L "$socket" send-keys -t "$session:0.0" Enter
wait_for_pane "$socket" "$session:0.0" "goodbye"
sleep 0.5

tmux -L "$socket" new-window \
    -t "$session" -n capture -c "$work_dir" \
    "env PATH='$work_dir/bin:$PATH' PS1='$ ' TERM=xterm-256color \
        bash --noprofile --norc"
wait_for_pane "$socket" "$session:capture.0" "$"
sleep 0.5

capture_command='tmux capture-pane -pe -t :0 | console2svg capture -h 12 -o capture.svg'
type_slowly "$socket" "$session:capture.0" "$capture_command"
tmux -L "$socket" send-keys -t "$session:capture.0" Enter
wait_for_file "$work_dir/capture.svg"
wait_for_pane "$socket" "$session:capture.0" "Generated: capture.svg"
sleep 0.5

tmux -L "$socket" send-keys -t "$session:capture.0" -l "exit"
tmux -L "$socket" send-keys -t "$session:capture.0" Enter
wait_for_pane "$socket" "$session:0.0" "goodbye"

sleep 0.5
tmux -L "$socket" detach-client -s "$session"
wait "$recorder_pid"
recorder_pid=""

mv "$work_dir/capture.svg" "$DEMO_ROOT/assets/cmd-tmux-cap.svg"
mv "$work_dir/cmd-tmux-replay.svg" "$DEMO_ROOT/assets/cmd-tmux-replay.svg"
