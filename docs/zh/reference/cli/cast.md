---
title: cast
description: 渲染 asciicast v2 文件的命令。
---

```bash
console2svg cast <cast> [options]
```

将 asciicast v2 事件作为终端画面重放，并生成 SVG 或动画 SVG。

## 选项

### `-o, --out <path>`

### `--stdout`

指定输出文件，或将 SVG 写入标准输出。

### `-m, --mode <image|video>`

### `-v, --video`

选择输出模式，或输出动画 SVG。

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

指定终端宽度和高度。`adjust` 会根据输入自动调整。

### `--timeout <sec>`

### `--sleep <sec>`

### `--fadeout <sec>`

指定处理时长、完成后的等待时长和视频淡出时长。

### `--verbose [path]`

启用详细日志，并可选择将其保存到文件。

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

指定是否显示命令、标题和提示符。

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

指定窗口边框、桌面风格窗口内边距和不透明度。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

指定或覆盖主题、文字颜色和终端背景颜色。

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

指定外边距、内边距以及背景颜色或图像。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

指定字体、字号和 SVG 文本长度调整方式。

### `--mask <pattern>`

### `--mask-auto [bool]`

设置字符串遮盖和自动密钥遮盖。

### `--frame <index>`

### `--time <sec>`

### `--size <WxH>`

指定帧编号、时间和输出尺寸。

### `--save-frames <dir>`

将动画的每一帧保存到指定目录。

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

使用单位或文本位置裁剪画面的上下左右。

### `--no-loop`

### `--fps <number>`

### `--timing <deterministic|realtime>`

设置循环、最大采样率和视频计时控制。

### `--coalesce-ms <ms|auto>`

指定合并相邻输出事件的间隔。

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

设置鼠标追踪转发、PTY 颜色环境变量覆盖和 CI 环境变量删除。

### `--svg-converter <converter>`

指定 SVG 栅格化方式。
