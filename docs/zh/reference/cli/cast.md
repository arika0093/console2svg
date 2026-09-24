---
title: cast
description: 读取 asciicast v2 录制文件并渲染为 SVG 图片或视频的命令。
---

```bash title="Terminal"
console2svg cast <cast> [options]
```

`cast` 是一个子命令，用于读取由终端录制工具（如 asciinema）或 console2svg 本身生成的 **asciicast v2** 格式文件（`<cast>`），将其作为终端画面回放并转换为矢量图（SVG）或视频。
无需启动新的子进程，即可基于现有的录制数据高保真渲染静态图、动画 SVG、PNG、MP4 等多种格式。

## 命令行为

将指定 asciicast 文件中包含的终端输出事件和时间戳信息传入终端仿真器，重现画面状态。
指定 `-v` 时，会生成包含所有事件时序变化的动画 SVG 或视频；不指定时，则将最终到达的画面输出为单张静态图。

## 选项

除新命令执行相关选项（命令行指定、环境变量清理等）外，`cast` 支持与 `capture` 相同的渲染配置选项。

### 输出与格式

* `-o, --out <path>`：指定输出文件路径（根据扩展名自动判断格式）。
* `--format <format>`：明确指定输出格式（`svg`、`png`、`gif`、`mp4` 等）。
* `-v, --video`：以动画 SVG 或视频格式输出。
* `--stdout`：将生成的 SVG 直接输出到标准输出。

### 终端尺寸与画面裁剪

* `-w, --width <int|adjust>`, `-h, --height <int|adjust>`：指定终端宽度（字符数）与高度（行数）。指定 `adjust` 可根据内容自动调整。
* `--size <WxH>`：指定输出图片的像素尺寸。
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`：以像素单位（`px`）或字符单位（`ch`）裁剪画面的上下左右边缘。

### 外观与主题

* `-d, --window [style]`：指定窗口装饰样式（`macos`、`macos-pc`、`windows` 等）。
* `-t, --theme <id>`：指定外观主题的 ID。
* `--forecolor <color>`, `--backcolor <color>`：覆盖前景色或背景色。
* `--background <value>`：指定窗口背面的背景色或背景图片文件。
* `--opacity <number>`：指定终端背景的不透明度（`0.0`～`1.0`）。
* `--font <family>`, `--fontsize <px>`：指定字体系列与字体大小。
* `-c, --with-command`：在画面顶部显示命令标题栏。

### 回放与动画控制

* `--frame <index>`：提取指定帧编号并导出为静态图。
* `--time <sec>`：提取指定时间戳或 `START-END` 形式的区间并导出为静态图或动画。
* `--fps <number>`：指定最大采样率。
* `--no-loop`：禁用动画 SVG 的循环播放。
* `--sleep <sec>`：指定播放完成后最终帧的停留时间（以秒为单位）。
* `--fadeout <sec>`：指定视频末尾淡出效果的持续时间（以秒为单位）。
* `--save-frames <dir>`：将各帧的静态 SVG 图片按顺序编号保存至指定目录。

### 掩码与诊断

* `--mask <pattern>`：掩码指定的字符串模式。
* `--mask-auto [bool]`：设置是否启用基于 QuickLeaks 的自动机密掩码。
* `--svg-converter <converter>`：指定栅格化引擎（`auto`、`resvg`、`ffmpeg`、`rsvg-convert`）。
* `--verbose [path]`：指定详细日志的输出目标。
