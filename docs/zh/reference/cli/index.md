---
title: CLI 参考
description: 涵盖 console2svg 所有子命令与选项规范的完整参考。
---

`console2svg` 以子命令形式提供终端运行记录、矢量图与视频转换、主题管理以及运行环境诊断功能。
执行待捕获的命令时，请在 `--` 分隔符后指定所需运行的命令行，以避免选项误解析。

```bash title="Terminal"
console2svg capture [options] -- command [args...]
```

## 子命令列表

根据不同使用场景，提供了以下子命令：

### 屏幕录制与渲染

| 命令 | 作用 |
| --- | --- |
| [`capture`](./capture.md) | 在伪终端中运行指定命令，将最终画面或动画录制为 SVG/视频。 |
| [`interactive`](./interactive.md) | 启动交互式 Shell，随时通过按键操作交互式录制屏幕。 |
| [`replay`](./replay.md) | 重放预先录制的键盘输入，复现完全相同的操作结果并进行录制。 |
| [`cast`](./cast.md) | 读取现有的 asciicast v2 录制文件，并渲染为 SVG 图片或动画。 |

### 会话管理与外部集成

| 命令 | 作用 |
| --- | --- |
| [`session`](./session.md) | 启动并控制在后台持久运行的伪终端会话，读取当前屏幕状态。 |
| [`live-server`](./live-server.md) | 实时将运行中的终端画面转换为 SVG，并通过 HTTP 流式传输至浏览器。 |
| [`tmux`](./tmux.md) | 指定正在运行的 tmux 窗格，直接捕获其画面内容或进行实时流传输。 |

### 文档自动化与智能体支持

| 命令 | 作用 |
| --- | --- |
| [`scenario`](./scenario.md) | 读取场景文档，在伪终端中执行基于状态同步的自动化操作。 |
| [`batch`](./batch.md) | 批量扫描 Markdown/MDX 中的嵌入标记，自动生成并同步文档图片。 |
| [`llm`](./llm.md) | 输出内置的 Agent Skill 定义，使 AI 智能体（如 GitHub Copilot、Claude 等）能够操作 console2svg。 |

### 环境管理与实用工具

| 命令 | 作用 |
| --- | --- |
| [`theme`](./theme.md) | 列出、添加、更新和删除终端外观主题与调色板。 |
| [`status`](./status.md) | 诊断运行环境、各类渲染器（resvg、ffmpeg 等）以及可用功能。 |
| [`update`](./update.md) | 检查 console2svg 的最新版本并执行自更新。 |
| [`completions`](./completions.md) | 生成适用于 bash、zsh、fish、PowerShell 等的 Shell 补全脚本。 |
