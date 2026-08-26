#!/usr/bin/env bash

set -euo pipefail

DEMO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONSOLE2SVG_BIN="${CONSOLE2SVG_BIN:-console2svg}"

mkdir -p "$DEMO_ROOT/assets" "$DEMO_ROOT/logs"

wait_for_pane() {
    local socket="$1"
    local target="$2"
    local expected="$3"
    local pane=""

    for _ in $(seq 1 100); do
        pane="$(tmux -L "$socket" capture-pane -p -t "$target" 2>/dev/null || true)"
        if grep -Fq "$expected" <<<"$pane"; then
            return 0
        fi
        sleep 0.1
    done

    printf 'Timed out waiting for %q in tmux pane %s:\n%s\n' "$expected" "$target" "$pane" >&2
    return 1
}

wait_for_tmux_client() {
    local socket="$1"

    for _ in $(seq 1 100); do
        if tmux -L "$socket" list-clients >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.1
    done

    printf 'Timed out waiting for a client on tmux socket %s\n' "$socket" >&2
    return 1
}

send_key_until_pane() {
    local socket="$1"
    local target="$2"
    local key="$3"
    local expected="$4"
    local pane=""

    for _ in $(seq 1 5); do
        tmux -L "$socket" send-keys -t "$target" "$key"
        for _ in $(seq 1 20); do
            pane="$(tmux -L "$socket" capture-pane -p -t "$target" 2>/dev/null || true)"
            if grep -Fq "$expected" <<<"$pane"; then
                return 0
            fi
            sleep 0.1
        done
    done

    printf 'Timed out sending %s to tmux pane %s; expected %q:\n%s\n' \
        "$key" "$target" "$expected" "$pane" >&2
    return 1
}

wait_for_file() {
    local path="$1"

    for _ in $(seq 1 100); do
        if [[ -s "$path" ]]; then
            return 0
        fi
        sleep 0.1
    done

    printf 'Timed out waiting for generated file %s\n' "$path" >&2
    return 1
}

type_slowly() {
    local socket="$1"
    local target="$2"
    local text="$3"
    local index

    for ((index = 0; index < ${#text}; index++)); do
        tmux -L "$socket" send-keys -t "$target" -l "${text:index:1}"
        sleep 0.04
    done
}
