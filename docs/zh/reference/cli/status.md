---
title: status
description: 显示并诊断运行环境、各类渲染器及外部工具检测状态的命令。
---

```bash title="Terminal"
console2svg status [--format table|markdown|json]
console2svg status --json
```

`status` 是一个子命令，用于全面诊断并显示当前系统环境、SVG 渲染器（resvg、ffmpeg、rsvg-convert）、已安装主题以及各项功能的可用性。
可用于测试依赖环境的外部工具是否正常工作，并在故障排查或提交 Issue 时收集环境信息。

## 诊断项列表

执行该命令后，将按以下分类输出详细的检测结果：

* **Application**：console2svg 版本号、可执行文件绝对路径、Native AOT 应用状态、分发渠道
* **Platform**：操作系统名称及内核版本、CPU 架构、.NET 运行时版本
* **Renderers**：内置 resvg、rsvg-convert、ffmpeg 的检测状态、版本与路径，以及可用于 MP4 编码的编解码器（如 libx264）
* **Features**：各项功能（如视频录制 Video capture、tmux 窗格集成等）是否可用的判定
* **Themes**：已识别的内置主题和自定义主题数量
* **Colors / Formats**：终端的 ANSI 颜色支持情况及可用输出格式列表

## 选项

### `--format <table|markdown|json>`

指定输出格式：

* `table`（默认值）：以适合终端查看的彩色表格格式输出。
* `markdown`：以可直接粘贴到 GitHub Issue 或 Pull Request 的 Markdown 格式输出。
* `json`：以适合脚本处理和 CI 诊断步骤的结构化 JSON 格式输出。

### `--json`

与 `--format json` 相同，将诊断结果以 JSON 格式输出至标准输出。可用作快捷标志。
