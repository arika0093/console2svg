---
title: tmux
description: Command that records or streams tmux panes.
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`capture` records the specified pane to SVG, and `live-server` live-streams that pane. Use `--history` to specify how many history lines to retrieve.

## `capture` options

In `capture`, you can use the options for [`capture`](./capture.md).

### `--target <pane>`

Specify the tmux pane to record (example: `:0.1`).

### `--history [lines]`

Include all pane history or only the specified number of lines. If the value is omitted, the entire history is retrieved.

## `live-server` options

In `live-server`, you can use the options for [`live-server`](./live-server.md).

### `--target <pane>`

Specify the tmux pane to stream.
