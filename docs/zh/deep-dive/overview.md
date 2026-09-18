---
title: 设计思想
description: console2svg 的设计思想。
---

console2svg 是一个 CLI 工具，旨在将终端输出转换为准确、高质量的矢量图片（SVG）或视频。

## 设计思想

console2svg 基于以下设计思想开发。

### 到处都能用

我是 Windows 用户。（嗯，大多数人大概也是吧。）  
说实话，我已经厌倦了只写 `brew` 安装方式的工具。  

因此，它的目标是在 Linux/macOS/Windows 等主要平台上运行。


### 开箱即用

开发动机之一是，这类转换工具往往需要把各种东西高度组合起来使用，非常麻烦。  
console2svg 的目标是安装后即可使用。

* 在 Linux/macOS 环境中，除 `ffmpeg` 外的所有依赖都内置在二进制文件中。
* 在 Windows 环境中，发布归档内连同 `ffmpeg` 一并包含全部内容。
  * 捆绑的二进制文件使用 [btbN/FFmpeg-Builds](https://github.com/btbN/FFmpeg-Builds)。
  * 这是因为在 Windows 环境中另行安装 `ffmpeg` 并设置路径很麻烦。

### 不让用户多费事

console2svg 重视避免给用户增加额外麻烦。  
例如[交互式捕获](../basic-usage/interactive-capture.md)、[tmux 支持](../utilities/tmux.md)等功能。

> [!TIP]
> 如果你有想法，请务必提交 [Issue](https://github.com/arika0093/console2svg/issues)！


### 高质量输出

console2svg 旨在尽可能忠实地再现终端输出。  

> [!NOTE]
> 不过，完全再现仍然有很多课题。  
> 如果显示存在问题，请到 [Issue](https://github.com/arika0093/console2svg/issues) 报告。

### 控制包依赖

目前，console2svg 只使用以下依赖。[^1]

* [Porta.Pty](https://github.com/tomlm/Porta.Pty)
  * 跨平台 PTY 控制。
* [System.CommandLine](https://github.com/dotnet/command-line-api)
  * 命令行参数解析。
* [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api)
  * 支持[Shell 补全](../utilities/shell-completion.md)。
* [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing/)
  * 高速哈希计算（XxHash3）的实现。
  * 用于计算并复用终端输出的哈希值。
* [VYaml](https://github.com/hadashiA/VYaml)
  * 高速 YAML 序列化器。
* [ZLogger](https://github.com/Cysharp/ZLogger)
  * 高速日志输出实现。

此外，整个工具还使用以下外部依赖。

* [ffmpeg](https://ffmpeg.org/)
  * 用于视频生成的外部命令。
* [resvg](https://github.com/linebender/resvg)
  * 将 SVG 转换为 PNG 的高速实现。
* [betterleaks](https://github.com/betterLeaks/betterleaks)
  * 虽然不是直接依赖，但 [QuickLeaks](./conversion-process/quickleaks.md) 是基于其[定义文件](https://github.com/betterleaks/betterleaks/blob/main/config/betterleaks.toml)生成的。


[^1]: 不包括开发时依赖（分析器/测试相关等）。
