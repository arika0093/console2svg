---
title: interactive
description: 记录交互式 shell 或程序的命令。
---

```bash title="Terminal"
console2svg interactive [options]
```

启动交互式 shell，并使用录制键开始或结束画面录制。

## 选项

`interactive` 支持 `capture` 中与输出、终端显示、录制、遮盖和诊断相关的选项。不支持 `--in`、`--frame`、`--time`、`--replay` 系列和嵌入系列选项。

### `-o, --out <path>`

指定输出文件。

### `--stdout`

将 SVG 写入标准输出。

### `-m, --mode <image|video>`

指定输出模式。

### `-v, --video`

输出动画 SVG。

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

以字符数和行数指定终端宽度和高度。`adjust` 会根据输入自动调整。

### `--timeout <sec>`

在指定秒数后结束录制。

### `--save-cast <path>`

将录制的输出保存为 asciicast v2 文件。

### `-c, --with-command`

在输出开头显示执行的命令。

### `--header <text>`

### `--prompt <text>`

覆盖命令行标题或提示符。

### `-d, --window [style]`

指定窗口边框样式。省略时使用 `macos`。

### `--pc-padding <number>`

### `--opacity <number>`

指定桌面风格窗口的内边距和不透明度（`0` 到 `1`）。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

覆盖文字颜色或终端背景颜色。

### `--margin <number>`

### `--padding <number>`

指定窗口边框或 shell 内部的内边距。

### `--background <value>`

指定背景颜色或图像。指定两次可创建渐变。

### `--font <family>`

### `--fontsize <px>`

指定字体系列或字号。

### `--mask <pattern>`

### `--mask-auto [bool]`

设置指定字符串遮盖和自动密钥遮盖。自动遮盖默认启用。

### `--save-frames <dir>`

将动画的每一帧保存到指定目录。

### `--size <WxH>`

将输出尺寸指定为 `WIDTH`、`WIDTHx*`、`*xHEIGHT` 或 `WIDTHxHEIGHT`。

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

顶部和底部可按 `px`、`ch` 或文本位置裁剪，左右可按 `px` 或 `ch` 裁剪。

### `--no-loop`

不循环播放动画 SVG。

### `--fps <number>`

### `--timing <deterministic|realtime>`

指定最大采样率和视频计时控制方式。

### `--sleep <sec>`

### `--fadeout <sec>`

指定录制后的等待时长或视频淡出时长。

### `--coalesce-ms <ms|auto>`

指定合并相邻输出事件的间隔。

### `--no-resize`

保持初始 TTY 尺寸。

### `--mouse`

将鼠标追踪转发到 PTY。

### `--no-colorenv`

### `--no-delete-envs`

不覆盖 PTY 颜色环境变量，也不删除 CI 环境变量。

### `--adjust <mode>`

选择 SVG 文本长度调整方式：`spacing` 或 `spacingAndGlyphs`。

### `--svg-converter <converter>`

选择 SVG 栅格化方式：`auto`、`ffmpeg`、`rsvg-convert` 或 `resvg`。

### `--verbose [path]`

启用详细日志。指定值时，还会将日志保存到文件。
