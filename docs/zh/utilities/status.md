---
title: 系统状态检查
description: 使用 console2svg status 命令诊断运行环境、渲染器可用性及外部工具检测状态。
since: v0.9
---

`console2svg status` 命令用于检查并诊断当前运行环境、各渲染器的运作状态以及可用的系统功能。
在实际执行前，你可以通过该命令确认依赖外部工具的功能（如视频转换、多图像格式导出、tmux 集成等）能否正常工作。

## 通过实际运行验证工具

常规工具通常仅凭 `PATH` 中是否存在可执行文件来判定其是否可用，而 `status` 命令会真正将各个外部工具作为子进程启动，以获取其版本号和响应。
这一机制能够尽早发现“虽然在 PATH 中但无法运行”的环境问题，例如执行权限不足或软链接损坏等。

尤其是针对 SVG 渲染器，该命令会通过光栅化一个微型 SVG 样例进行实际探针测试（probe），仅将确认能够稳定运行的转换器报告为 `available`。

## 运行命令

不带任何参数执行时，命令将输出适配终端色彩的表格化结果。

```bash title="Terminal"
console2svg status
```

输出示例（具体内容与可用性会随操作系统与已安装软件包而变化）：

<!-- c2s::  -w 100 -- console2svg status -->

展示的诊断项分为以下几大类别：

* **Application**: console2svg 版本、安装分发渠道以及 Native AOT 编译状态
* **Platform**: 运行中的操作系统、CPU 架构以及 .NET 运行时版本
* **Renderers**: 内置 resvg、rsvg-convert、ffmpeg 的检测状态及 MP4 视频编解码器（如 libx264）
* **Features**: 视频录制、tmux 联动等各项功能的就绪判定
* **Themes**: 已识别的内置主题与自定义主题数量
* **Colors / Formats**: 终端 ANSI 色彩支持级别及支持导出的文件扩展名列表

## 切换输出格式

通过指定 `--format` 选项，可以选择满足不同使用场景的输出格式：

```bash title="Terminal" "--format markdown"
console2svg status --format markdown
```

* `--format table`（默认值）：专为终端控制台优化、带有色彩高亮的人类可读表格格式。
* `--format markdown`：可直接复制并粘贴到 GitHub Issue、Pull Request 或 Discussion 中的 Markdown 表格格式，非常适合反馈缺陷时附带环境信息。
* `--format json`（或 `--json`）：包含详尽结构化数据的 JSON 格式，适合脚本处理或 CI/CD 流水线自动化诊断。
