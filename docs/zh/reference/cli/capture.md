---
title: capture
description: 将终端输出记录为 SVG 的命令。
---

```bash
console2svg capture [options] -- command [args...]
```

在 PTY 中运行指定命令，并将结束时的画面保存为 SVG。添加 `-v` 可生成动画 SVG 或视频格式。

## 选项

### `-o, --out <path>`

指定输出文件。根据扩展名选择 SVG、PNG、GIF 等格式。

### `--stdout`

将 SVG 写入标准输出。

### `-m, --mode <image|video>`

选择 `image` 或 `video` 输出模式。

### `-v, --video`

输出动画 SVG，是 `--mode video` 的简写。

### `-w, --width <int|adjust>`

以字符数设置终端宽度。`adjust` 会根据输入自动调整。

### `-h, --height <int|adjust>`

以行数设置终端高度。`adjust` 会根据输入自动调整。

### `--timeout <sec>`

在指定秒数后结束录制。

### `--in <path>`

不运行命令，直接渲染输入的 asciicast v2 文件。

### `--save-cast <path>`

将录制的输出保存为 asciicast v2 文件。

### `--embed-cast`

将输入或录制的 asciicast 数据嵌入 SVG。

### `--embed-logs`

将诊断日志嵌入 SVG。

### `--embed-replay`

将录制的键盘输入嵌入 SVG。

### `--embed-debug`

将所有诊断信息嵌入 SVG。

### `--replay-save <path>`

将命令执行期间的键盘输入保存到文件，以便稍后重放。

### `--replay <path>`

重放已保存的键盘输入。

### `--verbose [path]`

启用详细日志。指定值时，还会将日志保存到文件。

### `-c, --with-command`

在输出开头显示执行的命令。

### `--header <text>`

覆盖命令行标题。

### `--prompt <text>`

指定提示符前缀。

### `-d, --window [style]`

指定窗口边框样式。省略时使用 `macos`。

### `--pc-padding <number>`

覆盖桌面风格窗口的内边距。

### `--opacity <number>`

将背景不透明度设置为 `0` 到 `1`。

### `-t, --theme <id>`

指定外观主题 ID，可多次指定。

### `--forecolor <color>`

覆盖终端文字颜色。

### `--backcolor <color>`

覆盖终端背景颜色。

### `--margin <number>`

设置窗口边框外边距。

### `--padding <number>`

设置 shell 内部的内边距。

### `--background <value>`

指定桌面背景颜色或图像。指定两次可创建渐变。

### `--font <family>`

指定 CSS 字体系列。

### `--fontsize <px>`

以像素指定字体大小。

### `--mask <pattern>`

遮盖输出中的指定字符串，可多次指定。

### `--mask-auto [bool]`

启用或禁用 Betterleaks 自动遮盖密钥。默认启用。

### `--frame <index>`

将视频或多帧输入中的指定帧作为静态图像输出。

### `--time <sec>`

将指定时间点或 `START-END` 格式的时间范围作为静态图像输出。

### `--size <WxH>`

指定输出尺寸。可使用 `WIDTH`、`WIDTHx*`、`*xHEIGHT` 或 `WIDTHxHEIGHT`。

### `--save-frames <dir>`

将动画的每一帧保存到指定目录。

### `--crop-top <value>`

按 `px`、`ch` 或文本位置裁剪顶部。

### `--crop-right <value>`

按 `px` 或 `ch` 裁剪右侧。

### `--crop-bottom <value>`

按 `px`、`ch` 或文本位置裁剪底部。

### `--crop-left <value>`

按 `px` 或 `ch` 裁剪左侧。

### `--no-loop`

不循环播放动画 SVG。

### `--fps <number>`

指定最大帧采样率。

### `--timing <deterministic|realtime>`

指定视频计时控制模式。

### `--sleep <sec>`

指定录制结束后的等待秒数。

### `--fadeout <sec>`

指定视频淡出时长。

### `--coalesce-ms <ms|auto>`

以毫秒指定合并相邻输出事件的间隔。`auto` 会自动设置。

### `--no-resize`

不调整终端大小，保持初始 TTY 尺寸。

### `--mouse`

在交互模式下将鼠标追踪转发到 PTY。

### `--no-colorenv`

不覆盖 PTY 的颜色环境变量。

### `--no-delete-envs`

不删除 CI 环境变量。

### `--adjust <mode>`

选择 SVG 文本长度调整方式：`spacing` 或 `spacingAndGlyphs`。

### `--svg-converter <converter>`

选择 SVG 栅格化方式：`auto`、`ffmpeg`、`rsvg-convert` 或 `resvg`。

```bash
console2svg capture -w 100 -h 24 -c -- fastfetch
```
