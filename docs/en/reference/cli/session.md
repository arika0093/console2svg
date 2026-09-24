---
title: session
description: Manage PTY sessions across console2svg invocations.
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list [--json]
console2svg session read <id> [--wait <duration>] [--json]
console2svg session send <id> (--keys <key> | --text <text>) [--json]
console2svg session resize <id> --width <columns> --height <rows> [--json]
console2svg session capture <id> [-o <path>] [appearance options] [--json]
console2svg session stop <id> [--json]
console2svg session stop --all [--yes] [--json]
```

Managed sessions let an agent start a TUI, read its current screen, send input,
and resize or stop it across separate CLI invocations. They are independent of
`interactive`, `live-server`, and tmux sessions.

`start` defaults to a 100x24 terminal and the current working directory.
`--width` and `--height` accept values from 1 to 500; `--cwd` selects another
working directory.

## Start and inspect

```bash title="Terminal"
console2svg session start --json -- btop
console2svg session read s_abc123 --wait 1s --json
```

The start result returns a `sessionId`, lifecycle `state`, process ID, and
terminal dimensions. Read results use the same `screen` shape as capture JSON:
`width`, `height`, plain-text `text`, and `truncated`. The response also
includes `state`, `exitCode` when available, a screen `version`, and
`timedOut`; a wait timeout is not an error. Text is capped at 200,000
characters. Waits are limited to 60 seconds.

## Send input and resize

```bash title="Terminal"
console2svg session send s_abc123 --text "search query"
console2svg session send s_abc123 --keys Enter
console2svg session send s_abc123 --keys Ctrl+C
console2svg session resize s_abc123 --width 120 --height 40
```

`--text` sends literal UTF-8 without adding a newline. `--keys` accepts
`Enter`, `Return`, `Tab`, `Escape`/`Esc`, `Backspace`, `Delete`, `Up`,
`Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`, `Ctrl+A`
through `Ctrl+Z`, or one printable character. Use a separate `read` to inspect
the resulting screen.

## Capture and stop

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg --json
console2svg session stop s_abc123
console2svg session stop --all --yes
```

`session capture` renders the current screen to SVG and supports the existing
appearance options. SVG is the only supported output format for this command.
`stop --all` only affects managed sessions and requires `--yes` when standard
input is redirected.

Exited sessions remain available for 24 hours, then are cleaned up. Stopping a
session removes its saved files, so it no longer appears in `session list` and
cannot be read or captured. If a worker becomes unreachable, the last saved
snapshot is shown with state `unavailable`. `session list` shows sessions
created by `session start` that have not been stopped.
