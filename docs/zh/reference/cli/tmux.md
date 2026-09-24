---
title: tmux
description: 指定正在运行的 tmux 窗格以获取屏幕快照或进行实时直播的命令。
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`tmux` 是一组子命令，用于针对已在运行的 **tmux**（终端复用器）窗格，在不中断正在运行的进程的前提下，直接捕获其画面状态或进行实时流传输。
你可以随时从外部将长时间运行的训练任务、构建进程或常驻开发服务器的画面拍摄为 SVG 并进行共享（适用于类 Unix 环境及 WSL）。

## 子命令

### `tmux capture`

将指定 tmux 窗格的当前画面记录为 SVG 图片。

```bash title="Terminal"
console2svg tmux capture --target %1 -o pane.svg -d macos
```

#### `--target <pane>`（必选）

指定目标 tmux 窗格的标识符。
可指定窗格 ID（`%0`、`%1`）、窗口/窗格编号（`:0.1`、`session:0.1`）或窗格标题等。

#### `--history [lines]`

不仅捕获屏幕当前可见区域，还包含回滚历史记录。
指定数字时回溯获取指定行数；省略参数仅指定 `--history` 时则获取缓冲区内的所有历史记录。

#### `--json`

将捕获结果以 JSON 格式输出至标准输出。
可获取窗格内的纯文本内容、尺寸以及生成图片的路径。

```bash title="Terminal"
console2svg tmux capture --target %1 --json
```

响应结构与 [`capture` 命令的 `--json`](./capture.md#json) 相同。

### `tmux live-server`

实时将指定 tmux 窗格的画面转换为 SVG，并通过 HTTP 向浏览器进行实时流式传输。

```bash title="Terminal"
console2svg tmux live-server --target :0.0 127.0.0.1:38473
```

#### `--target <pane>`（必选）

指定要推流的 tmux 窗格标识符。

#### `[host:port]`

指定监听的地址和端口号（默认值：`127.0.0.1:38473`）。

## 可用的通用选项

`tmux capture` 可以直接使用 [`capture`](./capture.md) 的外观与掩码选项（如 `-d`、`-t`、`--margin`、`--font`、`--mask` 等）。
同样地，`tmux live-server` 支持 [`live-server`](./live-server.md) 的推流与外观选项（如 `--fps`、`--mask-auto` 等）。
