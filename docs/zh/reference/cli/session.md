---
title: session
description: 跨 CLI 调用启动、操作和捕获后台终端会话的命令。
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session list --all
console2svg session read <id> [--structured]
console2svg session wait <id> --text <literal> [--until present|absent] [--stable-for <duration>] [--timeout <duration>]
console2svg session send <id> (--keys <key> | --text <text> | --paste <text> | --raw-hex <bytes>)
console2svg session resize <id> --width <columns> --height <rows>
console2svg session capture <id> [-o <path>] [appearance options]
console2svg session inspect <id> [appearance options]
console2svg session stop <id>
console2svg session stop --all [--yes]
```

`session` 是一组子命令，用于管理在后台独立运行的伪终端会话。
会话一旦启动，即可从后续不同的 CLI 调用中读取屏幕文本、发送按键输入或将当前显示状态捕获为 SVG 图片。
在通过 AI 智能体或自动化脚本单步执行与控制交互式 TUI 应用程序（编辑器、配置菜单、交互式 CLI 等）时，该功能尤为高效。

需要注意的是，**所有 `session` 子命令都会向标准输出输出结构化 JSON**（无需额外选项启用 JSON 输出）。诊断日志将独立输出至标准错误。

操作失败时也会在标准输出返回 JSON 错误封装，例如 `{"schemaVersion":1,"status":"error","error":{"code":"session_not_found","message":"..."}}`。程序应根据 `error.code` 分支；`error.message` 和标准错误诊断面向用户，文字可能调整。`session wait` 保留条件结果和屏幕信息，并在失败时添加 `status: "error"` 与 `error.code`。`session stop --all` 的 `failed` 数组包含每个会话的 `{sessionId, code, message}`。

稳定错误码包括 `session_not_found`、`session_expired`、`session_exited`、`host_unavailable`、`unsupported_key`、`invalid_condition`、`wait_timeout` 和 `cancelled`。其他代码为 `session_not_started`、`invalid_request`、`invalid_operation`、`permission_denied`、`io_error` 和 `session_error`。

主机一次处理一个 IPC 请求，并按连接被接受的顺序执行操作；一次复合 `send` 始终作为一个操作处理。屏幕读取等待最多100ms 后就会释放请求槽，因此长轮询不会长时间阻塞输入。等待主机响应的上限为5秒，取消 CLI 也会取消该等待。

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

以纯文本形式返回会话当前的屏幕内容。

```bash title="Terminal"
console2svg session read s_abc123
```

* `<id>`：目标会话 ID

响应中的 `screen` 对象包含终端宽高、屏幕纯文本（`text`，最多 200,000 字符）、截断状态、从 0 开始的光标行列及可见状态、备用屏幕状态、滚动缓冲区行数，以及 `scope: "viewport"`。指定 `--structured` 后，还会返回带版本号的逐单元格行优先快照，其中包含样式、超链接和宽字符信息。

```bash title="Terminal"
console2svg session read s_abc123 --structured
```

### 3. 等待屏幕文本：`wait`

等待指定文本出现在屏幕上或从屏幕消失。匹配区分大小写，并采用子字符串匹配。

```bash title="Terminal"
console2svg session wait s_abc123 --text "Hi! How can I help?"
console2svg session wait s_abc123 --text "Working" --until absent --stable-for 2s --timeout 3m
```

* `--text <literal>`：必需的匹配文本
* `--until <present|absent>`：等待文本出现（默认）或消失
* `--stable-for <duration>`：条件持续满足此时间后才成功（默认：不额外等待）
* `--timeout <duration>`：可选超时；没有最大限制。省略时一直等到条件满足、会话结束或命令被取消

使用 `--until absent` 时，目标文本必须先在屏幕上出现，之后消失才算匹配。时间单位支持 `ms`、`s`、`m`、`h`（例如 `500ms`、`2s`、`3m`、`1h`）；不带单位的数字按秒处理。
JSON 响应包含 `result`（`matched`、`timeout` 或 `session-ended`）、`matched`、`timedOut`、最新屏幕及其版本。只有匹配成功时退出码为 0；超时或会话结束时退出码为 1。无上限等待可用 Ctrl+C 取消。

### 4. 发送按键与文本：`send`

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

重复指定 `--text`、`--paste`、`--keys` 和 `--raw-hex` 时，会按命令行顺序通过一个主机请求发送，因此其他客户端无法在这些输入步骤之间插入内容。`--paste <text>` 与 `--text` 普通输入不同：如果应用启用了 bracketed paste 模式，文本会被终端的开始和结束标记包围。

`--raw-hex <bytes>` 会直接发送以十六进制数字对表示的非空字节序列（例如 `1B5B41` 表示发送 `ESC [ A`）。语义按键还包括 `Insert`、`F1`～`F12`、`Shift+Tab`、`Shift+Up`、`Ctrl+Alt+Left` 和 `Meta+Home`。小键盘语义按键包括 `KP0`～`KP9`、`KPDecimal`、`KPEnter`、`KPAdd`、`KPSubtract`、`KPMultiply`、`KPDivide` 和 `KPSeparator`；小键盘按键和未修饰的方向键会根据应用所选终端模式编码。`Alt` 与 `Meta` 会在可打印字符前添加 Escape；带修饰键的导航键和功能键使用 xterm 修饰序列。Raw 字节与语义按键不同，通过 `--raw-hex` 指定。

发送输入后，立即调用 `session read` 即可查看程序响应后的最新屏幕。

### 5. 调整窗口尺寸：`resize`

动态修改运行中虚拟终端窗口的尺寸。

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

向子进程发送 SIGWINCH（窗口大小改变信号），促使支持的 TUI 应用程序重绘画面。

### 6. 捕获当前屏幕：`capture`

将该会话当前的屏幕缓冲区保存为高质量静态 SVG 图片。

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`：输出目标 SVG 文件路径
* 外观选项：支持与 `capture` 相同的所有外观选项，包括窗口装饰（`-d`）、主题（`-t`）、文字与背景色、字体、边距等。

### 7. 目视检查当前屏幕：`inspect`

将该会话当前的屏幕缓冲区渲染为临时 SVG，便于目视检查，无需调用方指定输出路径。

```bash title="Terminal"
console2svg session inspect s_abc123 -d macos -t dracula
```

JSON 响应中会同时返回会话 ID 与生成的文件路径。`inspect` 与 `capture` 共用同一渲染链路，视觉保真度完全一致；适用于运行中的会话，也适用于 `capture` 可用的保留期已退出会话。外观选项与 `capture` 相同，但不支持 `-o`/`--out`、`--format` 与 `--stdout`。

临时文件行为：文件位于按用户隔离的系统临时目录下的随机目录（不可预测的 `inspect-<guid>` 子目录中的 `inspect.svg`）。按用户隔离的根目录与各检查目录均以私有权限创建（Linux/macOS 为 0700，Windows 为按用户隔离的临时目录），SVG 本身也以私有权限写入（Linux/macOS 为 0600）。每次执行新的检查时，会以尽力而为的方式清理超过 24 小时的旧检查文件；不会立即删除，以便调用方的智能体或客户端有足够时间读取图像。

`inspect` 是 Observation（观察），而 `capture` 是产生成果物的 Action：`inspect` 结果仅用于探索与诊断，会从 Scenario 导出中省略；`capture` 结果则是可导出的持久化成果物。

### 8. 查看会话列表：`list`

默认列出正在启动或运行中的会话。指定 `--all` 可包含仍在保留期内的已退出或不可用会话；可确定时，条目还会提供 `expiresAt`。

```bash title="Terminal"
console2svg session list --all
```

### 9. 终止会话：`stop`

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

停止会话后临时数据将被删除，之后无法再对其执行 `read`、`capture` 或 `inspect`。
