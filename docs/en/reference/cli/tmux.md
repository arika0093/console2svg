---
title: tmux
description: Subcommands to target active tmux panes to take screen snapshots or stream live.
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`tmux` is a suite of subcommands targeting panes in an already running **tmux** (terminal multiplexer) instance, allowing you to capture screen states directly or stream them live without interrupting running processes.
You can externally photograph and share screens of long-running machine learning jobs, build processes, or background development servers as SVGs at any time (available on Unix-like environments and WSL).

## Subcommands

### `tmux capture`

Records the current screen of the specified tmux pane as an SVG image.

```bash title="Terminal"
console2svg tmux capture --target %1 -o pane.svg -d macos
```

#### `--target <pane>` (required)

Specifies the target tmux pane identifier.
Accepts pane IDs (`%0`, `%1`), window/pane coordinates (`:0.1`, `session:0.1`), or pane titles.

#### `--history [lines]`

Captures scrollback history in addition to the visible screen area.
Specifying a number fetches that many lines back into history; specifying `--history` without arguments fetches all available history in the buffer.

#### `--json`

Outputs capture results in JSON format to standard output.
Retrieves plain text pane contents, dimensions, and generated image file paths.

```bash title="Terminal"
console2svg tmux capture --target %1 --json
```

The response structure is identical to [`capture` command's `--json`](./capture.md#json).

### `tmux live-server`

Converts the specified tmux pane screen to SVG in real time and streams it live over HTTP for browsers.

```bash title="Terminal"
console2svg tmux live-server --target :0.0 127.0.0.1:38473
```

#### `--target <pane>` (required)

Specifies the tmux pane identifier to stream.

#### `[host:port]`

Specifies the listening address and port number (default: `127.0.0.1:38473`).

## Available Common Options

`tmux capture` supports all appearance and masking options from [`capture`](./capture.md) (such as `-d`, `-t`, `--margin`, `--font`, `--mask`).
Similarly, `tmux live-server` supports streaming and appearance options from [`live-server`](./live-server.md) (such as `--fps`, `--mask-auto`).
