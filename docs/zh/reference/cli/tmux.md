---
title: tmux
description: 记录或流式传输 tmux 窗格的命令。
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`capture` 将指定窗格记录为 SVG，`live-server` 对该窗格进行实时配信。可以用 `--history` 指定回溯获取的历史行数。

## `capture` 的选项

`capture` 中可以使用 [`capture`](./capture.md) 的选项。

### `--target <pane>`

指定要记录的 tmux 窗格（例：`:0.1`）。

### `--history [lines]`

包含窗格的全部历史，或只包含指定行数。省略值时获取全部历史。

## `live-server` 的选项

`live-server` 中可以使用 [`live-server`](./live-server.md) 的选项。

### `--target <pane>`

指定要配信的 tmux 窗格。
