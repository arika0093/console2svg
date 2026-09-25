---
title: 与类似工具的对比
description: 与具备 console2svg 类似功能的工具对比。
---

终端输出的记录、渲染为图片、网络共享以及自动化，也可以通过组合现有工具来实现。
本文将 `console2svg` 的主要功能与对应领域的替代工具进行对比，从功能特性、擅长领域以及环境依赖等维度提供技术参考。

> [!NOTE]
> 本文旨在对代表性工具进行客观的技术对比，而非列举详尽的打分表。

## 单次终端图片生成

对应 [`capture`](../basic-usage/capturing-images/overview.mdx) 功能。执行指定的 CLI 命令，并将最终输出保存为静态图片。

| 工具 | 主要输出格式 | 主要功能与特性 | 外部依赖与运行环境 | 仓库指标 |
| :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | SVG（支持转换为 PNG/视频） | 直接通过命令生成 SVG、窗口装饰、外观主题、敏感信息脱敏 | 官方发布归档包（内置 resvg 原生库，Windows 归档内置 ffmpeg） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**asciinema**](https://github.com/asciinema/asciinema) + 转换工具 | asciicast（可转换为 SVG/GIF） | 终端事件轻量记录、Web 播放器、丰富的衍生生态 | Python/Rust（本体）+ svg-term-cli（Node.js）等 | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |
| [**termtosvg**](https://github.com/nbedos/termtosvg) | SVG（动画/静态帧） | 支持模板自定义 SVG 样式、基于 asciicast 渲染 | Python 3、pyte、lxml | 已归档（2020年） [![GitHub stars](https://img.shields.io/github/stars/nbedos/termtosvg)](https://github.com/nbedos/termtosvg) |
| [**tmux (`capture-pane`)**](https://github.com/tmux/tmux) | 纯文本 | 提取已有窗格内容或回滚历史缓冲区 | tmux（Unix 环境，C 语言） | [![GitHub last commit](https://img.shields.io/github/last-commit/tmux/tmux)](https://github.com/tmux/tmux) [![GitHub stars](https://img.shields.io/github/stars/tmux/tmux)](https://github.com/tmux/tmux) |
| [**termshot**](https://github.com/homeport/termshot) | PNG | 执行命令并直接生成带窗口边框的 PNG 截图 | Go 编写的单二进制文件，主要支持 macOS/Linux | [![GitHub last commit](https://img.shields.io/github/last-commit/homeport/termshot)](https://github.com/homeport/termshot) [![GitHub stars](https://img.shields.io/github/stars/homeport/termshot)](https://github.com/homeport/termshot) |

### asciinema + SVG/GIF 转换工具

[asciinema](https://docs.asciinema.org/) 是将终端输入输出及时间数据记录为 asciicast 格式（`.cast`）的代表性工具。由于只保存文本与时间戳，文件体积小，且在 Web 播放器中支持直接选中并复制文本。

asciinema 本身不直接输出 SVG 等图片文件。要生成静态或动画 SVG，需要配合 [svg-term-cli](https://github.com/marionebl/svg-term-cli) 或 [scenetake](https://github.com/guitarrapc/scenetake) 等外部转换工具；生成 GIF 则需使用 [agg](https://docs.asciinema.org/manual/agg/)。

### termtosvg

[termtosvg](https://github.com/nbedos/termtosvg) 是通过运行终端会话直接生成 SVG 动画或静态画面的 Python 工具。具备 SVG 模板机制，可自定义窗口外框与字体。

官方仓库已于 2020 年归档停止维护。运行需要 Python 环境及依赖库（`pyte`、`lxml`）。

### tmux

[tmux](https://man7.org/linux/man-pages/man1/tmux.1.html) 是终端复用器，其 `capture-pane` 命令可将正在运行的窗格内容或历史缓冲区抓取为包含转义序列的纯文本。

tmux 本身不具备图像渲染功能。要保存为图片，需要将抓取的文本交给其他渲染工具处理。

### termshot

[termshot](https://github.com/homeport/termshot) 是通过解析命令输出的 ANSI 颜色并生成带有窗口样式 PNG 图片的 Go 工具。

其输出仅支持光栅图片（PNG），不支持矢量格式（SVG）。主要面向 macOS 与 Linux 环境。

### console2svg

`console2svg capture` 集成了命令运行、终端仿真及 SVG 渲染。支持通过命令行参数配置窗口装饰（macOS 或 Windows 样式）、主题以及自动敏感信息脱敏。发布归档包中包含了用于 PNG 转换的本地库（`resvg`），Windows 版本归档包还内置了用于视频生成的 `ffmpeg`。

生成的静态 SVG 属于图片文件，不支持像 asciinema Web 播放器那样在播放中随时复制文本或调整播放倍速。此外，对于高度复杂的终端绘图，可能会因终端仿真器实现细节的差异而出现排版微调需求。

## 基于预设脚本生成图片

对应 [`replay`](../automation/replay.md) 与 `scenario` 功能。将操作步骤预先写在脚本或配置文件中，在 CI 或文档构建阶段自动生成可复现的演示图片或视频。

| 工具 | 场景定义格式 | 主要输出格式 | 主要功能与特性 | 外部依赖与运行环境 | 仓库指标 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | JSON（按键录制）或 Scenario（YAML/JSON） | SVG、GIF、MP4、WebM | 将按键重放至 PTY、验证（Verify）步骤、批量应用 SVG 样式 | 官方发布归档包（视频转换需 ffmpeg） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**scenetake**](https://github.com/guitarrapc/scenetake) | YAML | asciicast v3、动画 SVG | 声明式命令序列、通过 `pty: true` 实时录制、行高亮配置 | .NET 工具 / npm / 独立二进制包 | [![GitHub last commit](https://img.shields.io/github/last-commit/guitarrapc/scenetake)](https://github.com/guitarrapc/scenetake) [![GitHub stars](https://img.shields.io/github/stars/guitarrapc/scenetake)](https://github.com/guitarrapc/scenetake) |
| [**Charmbracelet VHS**](https://github.com/charmbracelet/vhs) | Tape（自定义脚本语言） | GIF、MP4、WebM、PNG（截图） | 模拟打字动画、丰富主题、自动化测试 | Go 二进制文件 + ttyd + ffmpeg | [![GitHub last commit](https://img.shields.io/github/last-commit/charmbracelet/vhs)](https://github.com/charmbracelet/vhs) [![GitHub stars](https://img.shields.io/github/stars/charmbracelet/vhs)](https://github.com/charmbracelet/vhs) |

### guitarrapc/scenetake

[scenetake](https://github.com/guitarrapc/scenetake) 通过在 YAML 文件中声明待执行的命令列表，并在执行后生成 asciicast v3 或动画 SVG。支持配置输出行的高亮和模拟打字抖动（jitter），在配置了 `pty: true` 的步骤中可通过伪终端进行实时流录制。

适合使用 YAML 维护命令并在构建文档时自动抓取日志演示的场景。

### Charmbracelet VHS

[VHS](https://github.com/charmbracelet/vhs) 通过在 `.tape` 文本文件中编写按键与等待时间，以代码方式生成终端操作演示视频（GIF/MP4/WebM）。

适用于生成带有打字动画效果的演示视频。其底层需要系统环境中安装有 Web 终端服务 `ttyd` 以及视频编码工具 `ffmpeg`。它不支持导出矢量格式的 SVG。

### console2svg

`console2svg replay` 读取记录了按键与时间间隔的 JSON 文件，并重新发送至 PTY 中重现交互画面。`console2svg scenario` 则根据 YAML/JSON 描述的生命周期（准备、执行、验证、清理）运行测试，并输出 SVG 或视频。

它不包含类似 VHS 的拟人化打字抖动效果，也不支持在 YAML 中直接配置逐行高亮语法。

## 交互操作期间的截图捕获

对应 [`interactive`](../basic-usage/interactive-capture.md) 功能。用户在手动操作 Shell 或 TUI 的过程中，在任意时刻进行截图或录屏。

| 工具 | 操作方式 | 主要输出格式 | 主要功能与特性 | 外部依赖与运行环境 | 仓库指标 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | 在 PTY 中操作，按快捷键（F9/F10）触发保存 | SVG（静态）、GIF/MP4（视频） | 不中断工作流程，按需直接截取矢量图片或视频片段 | 官方发布归档包（视频转换需 ffmpeg） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**asciinema**](https://github.com/asciinema/asciinema) | 使用 `rec` 开启，退出 Shell 时保存 | asciicast | 会话全程完整记录、后续回放与格式转换 | 单二进制文件（v3） | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |
| [**Terminalizer**](https://github.com/faressoft/terminalizer) | 使用 `record` 开启，编辑 YAML 后渲染 | GIF、Web 播放器 | 在 YAML 中编辑记录的帧序列、导出 Web 播放器 | Node.js、C++ 编译工具（node-gyp） | [![GitHub last commit](https://img.shields.io/github/last-commit/faressoft/terminalizer)](https://github.com/faressoft/terminalizer) [![GitHub stars](https://img.shields.io/github/stars/faressoft/terminalizer)](https://github.com/faressoft/terminalizer) |

### asciinema

[asciinema](https://docs.asciinema.org/manual/cli/) 将会话从开始到结束整体记录为一条数据流。如需静态图片，通常需要先录制完整会话，再通过外部工具抽取特定时间点的帧画面。

### Terminalizer

[Terminalizer](https://github.com/faressoft/terminalizer) 录制交互式会话后生成 YAML 文件，允许用户逐帧剔除不需要的动作或调整延迟时间，最后渲染为 GIF 或 Web 播放页面。需要 Node.js 及原生扩展编译环境（`node-gyp`）。

### console2svg

`console2svg interactive` 允许用户在正常操作终端的同时，通过按下功能键（`F10` 截取静态图，`F9` 录制视频）随时输出带有时间戳的 SVG 或视频文件。

它不包含类似 Terminalizer 的事后逐帧 YAML 编辑功能；按键瞬间的终端画面将被直接写入文件。

## 实时画面中继与 Web 共享

对应 [`live-server`](../utilities/live-server.md) 功能。将终端当前显示的画面实时中继到 Web 浏览器中。

| 工具 | 传输协议 | 浏览器端显示形式 | 交互操作（输入） | 外部服务器依赖 | 仓库指标 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | HTTP / Server-Sent Events（SSE） | 三层分离的 SVG（矢量图） | 无（仅供观看） | 无（内置服务器） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**ttyd**](https://github.com/tsl0922/ttyd) | WebSocket | xterm.js（Canvas / WebGL） | 支持（通过 `-W` 允许写入） | 无（内置服务器） | [![GitHub last commit](https://img.shields.io/github/last-commit/tsl0922/ttyd)](https://github.com/tsl0922/ttyd) [![GitHub stars](https://img.shields.io/github/stars/tsl0922/ttyd)](https://github.com/tsl0922/ttyd) |
| [**GoTTY**](https://github.com/yudai/gotty) | WebSocket | xterm.js / hterm | 支持（通过 `-w` 允许写入） | 无（内置服务器） | [![GitHub last commit](https://img.shields.io/github/last-commit/yudai/gotty)](https://github.com/yudai/gotty) [![GitHub stars](https://img.shields.io/github/stars/yudai/gotty)](https://github.com/yudai/gotty) |
| [**WeTTY**](https://github.com/butlerx/wetty) | WebSocket | xterm.js | 支持（基于 SSH） | 无（Node.js 服务器） | [![GitHub last commit](https://img.shields.io/github/last-commit/butlerx/wetty)](https://github.com/butlerx/wetty) [![GitHub stars](https://img.shields.io/github/stars/butlerx/wetty)](https://github.com/butlerx/wetty) |
| [**asciinema streaming**](https://github.com/asciinema/asciinema) | WebSocket（ALiS / asciicast） | asciinema-player | 无（仅供观看） | 需要（asciinema-server） | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |

### ttyd / GoTTY / WeTTY

[ttyd](https://github.com/tsl0922/ttyd) 与 [GoTTY](https://github.com/yudai/gotty) 是在浏览器中运行终端仿真器（如 xterm.js）并通过 Web 远程操作 Shell 的服务端工具。[WeTTY](https://github.com/butlerx/wetty) 则是通过浏览器进行 SSH 连接的 Node.js 工具。

这类工具的主要目的是接受浏览器端的用户输入并交互式运行命令。其前端显示基于 DOM 或 Canvas 渲染，不输出或分发 SVG 矢量图。

### asciinema live streaming

[asciinema CLI](https://docs.asciinema.org/manual/server/streaming/) 通过 WebSocket 将终端事件流传输至中继服务器（asciinema-server），从而允许多个观众在 Web 播放器中同时观看实时直播。

这种架构需要在发布者（CLI）与观众（浏览器）之间架设 asciinema-server。

### console2svg

`console2svg live-server` 在服务端将终端画面实时渲染为 SVG，并通过 Server-Sent Events（SSE）作为单向矢量数据流推送给浏览器。由内置的 HTTP 服务直接完成分发，无需外部中继服务器。

它不接受浏览器端的键盘与 Shell 输入。此外，由于通过 HTTP/SSE 分发完整的 SVG 标记（或分层增量），相较于基于 WebSocket 传输原始文本流的方案，其网络数据传输量会相对偏大。主要适用于直播软件（如 OBS Studio）将其作为本地浏览器源进行高清画面采集。

## 面向 LLM 的检查与验证循环

对应 [`session`](../for-llm/session.md) 功能。支持 AI 编程 Agent 或脚本程序启动后台伪终端，在检查终端视觉与文本状态的同时自主完成多步骤操作。

| 工具 | 控制接口 | 画面状态读取格式 | 等待与同步机制 | 支持平台 | 仓库指标 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg session**](https://github.com/arika0093/console2svg) | CLI（JSON 输入/输出） | 结构化文本、单元格坐标、SVG 图片 | 指定文本出现/消失等待（`wait`） | Linux, macOS, Windows | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**microsoft/tui-test**](https://github.com/microsoft/tui-test) | CLI、Rust、Python、Node.js | 终端文本、HTML/Trace、SVG 截图 | `expect text`、点击操作 | Linux, macOS, Windows | [![GitHub last commit](https://img.shields.io/github/last-commit/microsoft/tui-test)](https://github.com/microsoft/tui-test) [![GitHub stars](https://img.shields.io/github/stars/microsoft/tui-test)](https://github.com/microsoft/tui-test) |
| [**pproenca/agent-tui**](https://github.com/pproenca/agent-tui) | CLI（JSON/Text）、WebSocket | 终端文本、ANSI 截图 | `wait`（画面稳定/文本匹配） | Unix 类（Linux, macOS） | [![GitHub last commit](https://img.shields.io/github/last-commit/pproenca/agent-tui)](https://github.com/pproenca/agent-tui) [![GitHub stars](https://img.shields.io/github/stars/pproenca/agent-tui)](https://github.com/pproenca/agent-tui) |
| [**tmux + MCP 服务**](https://github.com/nickgnd/tmux-mcp) | Model Context Protocol（JSON-RPC） | 窗格纯文本 | 无（需客户端自行轮询） | Unix 类（tmux 运行环境） | [![GitHub last commit](https://img.shields.io/github/last-commit/nickgnd/tmux-mcp)](https://github.com/nickgnd/tmux-mcp) [![GitHub stars](https://img.shields.io/github/stars/nickgnd/tmux-mcp)](https://github.com/nickgnd/tmux-mcp) |

### microsoft/tui-test

[microsoft/tui-test](https://github.com/microsoft/tui-test) 专注于 TUI 应用与 Shell 的测试及自动化。除 CLI 外，还提供了 Rust、Python、Node.js 的语言绑定，支持在进程内直接驱动终端。

支持等待文本出现（`expect text`）、模拟鼠标点击指定文本、生成 HTML 追踪视图以及截取 SVG 图像。

### pproenca/agent-tui

[pproenca/agent-tui](https://github.com/pproenca/agent-tui) 是用于让 AI Agent 操作 TUI 程序的 Rust CLI 工具。由后台守护进程管理多个 PTY 会话，支持输入文本、发送按键以及画面稳定等待条件（Wait conditions）。

面向 Linux 和 macOS 等 Unix 类系统，不提供对原生 Windows 环境的支持。

### tmux + MCP 服务

[nickgnd/tmux-mcp](https://github.com/nickgnd/tmux-mcp) 等 MCP 协议服务将 tmux 的命令封装为标准接口，供 LLM 客户端操控现有终端。

主要用于向现有 tmux 窗格发送按键并获取其中的纯文本。由于未内置画面渲染结束的判定指令，需要客户端自行轮询文本状态。

### console2svg

`console2svg session` 由后台守护进程保持 PTY 会话，并通过各子命令（`start`、`send`、`read`、`wait`、`capture`、`stop`）返回包含文本、光标与字符样式的结构化 JSON。可直接截取 SVG 图片供多模态模型判断，并跨平台支持 Linux、macOS 与 Windows（ConPTY）。

与 `microsoft/tui-test` 不同的是，它不提供 Python 或 Node.js 的进程内库绑定，所有调用均通过 CLI 进程交互进行。此外，它未集成类似 Playwright 的鼠标文本点击解析或基于 Web 的追踪视图。
