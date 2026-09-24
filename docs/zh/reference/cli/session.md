---
title: session
description: 在多次 console2svg 调用之间管理 PTY 会话。
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

Managed session 允许 Agent 启动 TUI、读取当前画面、发送输入，并在不同的 CLI 调用中调整大小或停止。它与 `interactive`、`live-server` 和 tmux 会话相互独立。
`start` 默认使用 100x24 终端和当前工作目录。`--width` 和 `--height` 接受 1 到 500；`--cwd` 可指定其他工作目录。

## 启动和检查

```bash title="Terminal"
console2svg session start --json -- btop
console2svg session read s_abc123 --wait 1s --json
```

start 结果包含 `sessionId`、生命周期 `state`、进程 ID 和终端尺寸。read 结果使用与 capture JSON 相同的 `screen` 结构：`width`、`height`、纯文本 `text` 和 `truncated`。响应还包含 `state`、可用时的 `exitCode`、画面 `version` 和 `timedOut`；等待超时不是错误。文本最多 200,000 个字符。
等待时间最长为 60 秒。

## 发送输入和调整尺寸

```bash title="Terminal"
console2svg session send s_abc123 --text "search query"
console2svg session send s_abc123 --keys Enter
console2svg session send s_abc123 --keys Ctrl+C
console2svg session resize s_abc123 --width 120 --height 40
```

`--text` 按原样发送 UTF-8 文本，不会自动添加换行。`--keys` 支持 `Enter`、`Return`、`Tab`、`Escape`/`Esc`、`Backspace`、`Delete`、`Up`、`Down`、`Left`、`Right`、`Home`、`End`、`PageUp`、`PageDown`、`Ctrl+A` 至 `Ctrl+Z`，或一个可打印字符。使用单独的 `read` 检查操作后的画面。

## 捕获和停止

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg --json
console2svg session stop s_abc123
console2svg session stop --all --yes
```

`session capture` 将当前画面渲染为 SVG，并支持现有外观选项。此命令仅支持 SVG 输出。`stop --all` 只影响 managed session；标准输入被重定向时必须指定 `--yes`。

已退出的会话记录保留 24 小时，之后会被清理。停止会话时会删除其保存文件，因此它不会再出现在 `session list` 中，也无法再读取或捕获。如果 worker 无法连接，会返回最后保存的画面并将状态标记为 `unavailable`。`session list` 显示通过 `session start` 创建且尚未停止的会话。
