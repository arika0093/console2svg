---
title: replay
description: 自动重放已保存的键盘输入并捕获相同操作结果的命令。
---

```bash title="Terminal"
console2svg replay <replay.json> [options] -- command [args...]
```

`replay` 是一个子命令，用于读取预先通过 `capture --replay-save <path>` 等方式记录的键盘输入文件（`<replay.json>`），在向指定命令按相同时间节奏自动发送按键输入的同时记录屏幕。
它能够完整复现人工执行的交互式命令行操作（如编辑器操作、CLI 菜单选择等），非常适合自动生成最新的输出画面或动画 SVG。

## 命令行为

执行 `replay` 时，指定命令将通过 PTY（伪终端）启动，并在预定时刻发送重放文件中记录的各项按键操作（Enter、方向键、Ctrl 组合键、文本输入等）。
在所有按键发送完毕，并达到指定的等待时长或命令结束时，执行最终画面或视频的生成处理。

## 选项

除按键输入文件外，`replay` 支持与 `capture` 相同的输出设置、外观主题及视频选项。

### 输出与格式

* `-o, --out <path>`：指定输出文件路径（根据扩展名自动判断格式）。
* `--format <format>`：明确指定输出格式（`svg`、`png`、`gif`、`mp4` 等）。
* `-v, --video`：以动画 SVG 或视频格式输出。
* `--stdout`：将生成结果写入标准输出。

### 回放与时序控制

* `--timeout <sec>`：指定整个重放执行的超时时间（以秒为单位）。
* `--sleep <sec>`：指定全部按键输入完成后，结束录制前的额外等待秒数。
* `--fadeout <sec>`：指定视频末尾淡出效果的持续时间（以秒为单位）。
* `--fps <number>`：指定视频采样的最大帧率。
* `--timing <deterministic|realtime>`：选择视频回放的时序控制方式。

### 终端尺寸与画面裁剪

* `-w, --width <int|adjust>`, `-h, --height <int|adjust>`：指定终端宽度（字符数）与高度（行数）。
* `--size <WxH>`：指定输出图片的像素尺寸。
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`：以像素或字符单位裁剪画面的上下左右边缘。

### 外观与主题

* `-d, --window [style]`：指定窗口装饰样式（`macos`、`macos-pc`、`windows` 等）。
* `-t, --theme <id>`：指定外观主题的 ID。
* `--forecolor <color>`, `--backcolor <color>`：覆盖前景色或背景色。
* `--background <value>`：指定窗口背面的背景色或图片。
* `--opacity <number>`：指定终端背景的不透明度（`0.0`～`1.0`）。
* `--font <family>`, `--fontsize <px>`：指定字体系列与字体大小。
* `-c, --with-command`：在画面顶部显示执行的命令行。

### 录制与诊断信息嵌入

* `--save-cast <path>`：将重放时的终端输出保存为 asciicast v2 文件。
* `--embed-cast`：将原始 asciicast 数据作为元数据嵌入生成的 SVG 内部。
* `--embed-replay`：将使用的键盘重放数据嵌入 SVG 内部。
* `--embed-logs`, `--embed-debug`：将诊断日志或调试信息嵌入 SVG 内部。
* `--mask <pattern>`, `--mask-auto [bool]`：设置字符串掩码与自动机密保护。
* `--svg-converter <converter>`：指定栅格化引擎（`auto`、`resvg`、`ffmpeg`、`rsvg-convert`）。
* `--verbose [path]`：指定详细日志的输出目标。
