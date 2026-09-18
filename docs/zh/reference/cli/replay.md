---
title: replay
description: 重放已保存键盘输入的命令。
---

```bash title="Terminal"
console2svg replay <replay.json> [options] -- command [args...]
```

重放使用 `--replay-save` 保存的输入文件，以重复相同操作。

## 选项

`replay` 支持 `capture` 中可用的输出、终端显示、录制、遮盖和诊断选项，并额外指定输入文件。

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

指定录制时限和录制结束后的等待时长。

### `--save-cast <path>`

将重放结果保存为 asciicast v2 文件。

### `--embed-cast`

### `--embed-logs`

### `--embed-replay`

### `--embed-debug`

将 asciicast 数据、诊断日志、键盘输入或所有诊断信息嵌入 SVG。

### `--replay <path>`

### `--replay-save <path>`

指定包含额外键盘输入的重放文件，或保存输入的文件。

### `--verbose [path]`

启用详细日志，并可选择将其保存到文件。

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

指定是否显示命令、标题和提示符。

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

指定窗口边框、桌面风格窗口内边距和不透明度（`0` 到 `1`）。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

指定或覆盖主题、文字颜色和终端背景颜色。

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

指定窗口边框和 shell 内部的内边距，以及背景颜色或图像。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

指定字体、字号和 SVG 文本长度调整方式。

### `--mask <pattern>`

### `--mask-auto [bool]`

设置字符串遮盖和自动密钥遮盖。

### `--frame <index>`

### `--time <sec>`

指定作为静态图像输出的帧编号或时间（也可使用 `START-END` 范围）。

### `--size <WxH>`

将输出尺寸指定为 `WIDTH`、`WIDTHx*`、`*xHEIGHT` 或 `WIDTHxHEIGHT`。

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

### `--fadeout <sec>`

### `--coalesce-ms <ms|auto>`

指定淡出时长和合并输出事件的间隔。

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

设置鼠标追踪转发、PTY 颜色环境变量覆盖和 CI 环境变量删除。

### `--svg-converter <converter>`

指定 SVG 栅格化方式。
