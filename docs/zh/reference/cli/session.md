---
title: session
description: 跨 CLI 调用启动、操作和捕获后台终端会话的命令。
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session read <id> [--wait <duration>]
console2svg session send <id> (--keys <key> | --text <text>)
console2svg session resize <id> --width <columns> --height <rows>
console2svg session capture <id> [-o <path>] [appearance options]
console2svg session stop <id>
console2svg session stop --all [--yes]
```

`session` 是一组子命令，用于管理在后台独立运行的伪终端会话。
会话一旦启动，即可从后续不同的 CLI 调用中读取屏幕文本、发送按键输入或将当前显示状态捕获为 SVG 图片。
在通过 AI 智能体或自动化脚本单步执行与控制交互式 TUI 应用程序（编辑器、配置菜单、交互式 CLI 等）时，该功能尤为高效。

需要注意的是，**所有 `session` 子命令都会向标准输出输出结构化 JSON**（无需额外选项启用 JSON 输出）。诊断日志将独立输出至标准错误。

## 子命令列表与操作流程

### 1. 启动会话：`start`

作为后台工作进程启动命令并开启新的终端会话。

```bash title="Terminal"
console2svg session start --width 120 --height 30 -- btop
```

* `--width <columns>`：终端宽度（1～500，默认值：`100`）
* `--height <rows>`：终端高度（1～500，默认值：`24`）
* `--cwd <path>`：执行命令的工作目录

响应中包含唯一的 `sessionId`（例如：`s_abc123`）、进程生命周期状态（`state`）、OS 进程 ID 以及终端尺寸。

### 2. 读取屏幕状态：`read`

以纯文本形式获取会话当前的屏幕内容。

```bash title="Terminal"
console2svg session read s_abc123 --wait 2s
```

* `<id>`：目标会话 ID
* `--wait <duration>`：等待屏幕发生变化的持续时间（例如：`500ms`、`2s`；最长 60 秒）

响应中的 `screen` 对象包含终端宽高、屏幕纯文本（`text`，最多 200,000 字符）以及是否被截断（`truncated`）。
指定等待时间后，一旦检测到屏幕变化或达到超时时间，便会返回最新画面（超时并非错误，而是作为正常响应返回 `timedOut: true`）。

### 3. 发送按键与文本：`send`

向正在运行的程序发送键盘输入或文本字符串。

```bash title="Terminal"
# 输入字符串
console2svg session send s_abc123 --text "git status"
# 发送特殊按键
console2svg session send s_abc123 --keys Enter
# 发送控制键（如 Ctrl+C）
console2svg session send s_abc123 --keys Ctrl+C
```

* `<id>`：目标会话 ID
* `--text <text>`：原样发送指定字符串，不附加换行符。
* `--keys <key>`：发送特殊按键。支持的按键包括：`Enter`、`Tab`、`Escape`（`Esc`）、`Backspace`、`Delete`、`Up`、`Down`、`Left`、`Right`、`Home`、`End`、`PageUp`、`PageDown`、`Ctrl+A`～`Ctrl+Z`，或任意单个可打印字符。

发送输入后，立即调用 `session read` 即可查看程序响应后的最新屏幕。

### 4. 调整窗口尺寸：`resize`

动态修改运行中虚拟终端窗口的尺寸。

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

向子进程发送 SIGWINCH（窗口大小改变信号），促使支持的 TUI 应用程序重绘画面。

### 5. 捕获当前屏幕：`capture`

将该会话当前的屏幕缓冲区保存为高质量静态 SVG 图片。

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`：输出目标 SVG 文件路径
* 外观选项：支持与 `capture` 相同的所有外观选项，包括窗口装饰（`-d`）、主题（`-t`）、文字与背景色、字体、边距等。

### 6. 查看会话列表：`list`

获取当前所有启动中的会话列表。

```bash title="Terminal"
console2svg session list
```

### 7. 终止会话：`stop`

停止会话，结束关联的进程树并清理资源。

```bash title="Terminal"
# 停止单个会话
console2svg session stop s_abc123

# 一次性停止所有托管会话
console2svg session stop --all --yes
```

* `<id>`：要终止的会话 ID
* `--all`：一次性停止 console2svg 管理的所有会话。
* `-y, --yes`：跳过批量停止时的确认提示（在管道执行或自动化脚本中必须指定）。

停止会话后临时数据将被删除，之后无法再对其执行 `read` 或 `capture`。
