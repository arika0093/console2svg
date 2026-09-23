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

为提高可维护性，console2svg 的目标是尽可能少使用[软件包](./license.md)。
