---
title: capture
description: 执行终端命令并将输出录制为矢量图（SVG）或视频的命令。
---

```bash title="Terminal"
console2svg capture [options] -- command [args...]
```

`capture` 是最核心的基础子命令，通过 PTY（伪终端）执行指定命令，并将命令退出时的画面状态保存为 SVG 图片。
指定 `-v`（或 `--video`）选项时，可将执行过程中的画面变化录制为动画 SVG 或各类视频格式（MP4、WebM、GIF）。

## 输出目标与格式

### `-o, --out <path>`

指定输出文件路径。
根据扩展名（`.svg`、`.png`、`.gif`、`.mp4`、`.webm` 等）自动判断输出格式。省略时默认输出为 `output.svg`。

### `--format <svg|png|jpg|webp|gif|mp4|webm>`

明确指定输出格式。
此选项的优先级高于 `-o` 所指定文件的扩展名。

### `--stdout`

不将生成的 SVG 内容保存为文件，而是直接输出至标准输出。适用于通过管道传递给其他工具。

### `-m, --mode <image|video>`

选择输出模式：静态图（`image`）或视频（`video`）。

### `-v, --video`

输出动画 SVG 或视频。此为 `-m video` 的简写形式。

## 终端尺寸与画面裁剪

### `-w, --width <int|adjust>`

以字符数（列数）指定终端宽度。默认值为 `100`。
指定 `adjust` 可根据输出字符串的最长行自动调整宽度。

### `-h, --height <int|adjust>`

以文本行数指定终端高度。默认值为 `24`。
指定 `adjust` 可根据输出的实际总行数自动调整高度。

### `--size <WxH>`

指定栅格化时的输出像素尺寸。
支持类似 `1200x800` 同时指定宽高，也支持保持宽高比仅指定单边的 `1200x*` 或 `*x800` 记法。

### `--crop-top <value>`, `--crop-bottom <value>`

在指定范围内裁剪画面的上下边缘。
支持以像素（`10px`）、字符单元格（`2ch`）或特定字符串的出现位置（`"build finished"`）指定裁剪边界。

### `--crop-left <value>`, `--crop-right <value>`

在指定范围内裁剪画面的左右边缘。以像素（`10px`）或字符单元格（`4ch`）指定。

## 外观与主题设置

### `-d, --window [style]`

为终端外框添加窗口装饰（窗口框架）。
省略样式名称时默认应用 `macos`。主要样式包括 `macos`、`macos-pc`（带边距）、`windows`、`none`（无装饰）等。

### `-t, --theme <id>`

指定应用的外观主题 ID。可多次指定以叠加应用调色板与窗口主题。

### `--forecolor <color>`, `--backcolor <color>`

分别覆盖终端默认的文字颜色或背景颜色。使用 `#ffffff` 格式的十六进制颜色代码等进行指定。

### `--opacity <number>`

指定终端背景的不透明度，范围为 `0.0`（完全透明）至 `1.0`（完全不透明）。

### `--background <value>`

指定放置在窗口背面的桌面背景色或背景图片文件路径。
连续指定两个颜色代码时，会自动生成平滑的渐变背景。

### `--font <family>`

指定 SVG 中使用的 CSS 字体系列（例如：`JetBrains Mono, monospace`）。

### `--fontsize <px>`

以像素为单位指定字体大小（默认值：`14`）。

### `-c, --with-command`

在终端画面最顶部将实际执行的命令行（例如：`$ npm test`）显示为标题栏。

### `--header <text>`, `--prompt <text>`

覆盖 `-c` 显示的命令行标题栏的整体文本，或覆盖提示符符号（默认值：`$`）。

### `--margin <number>`, `--padding <number>`, `--pc-padding <number>`

以数值微调窗口外边距、终端内部内边距以及桌面边框留白。

## 动画与视频控制

### `--fps <number>`

指定视频或动画 SVG 的最大帧率（每秒采样次数）。

### `--no-loop`

禁用动画 SVG 的自动循环播放，播放完毕后停留在最终帧。

### `--sleep <sec>`

指定视频播放结束后最终帧的停留时间（以秒为单位），防止立即循环回到开头。

### `--fadeout <sec>`

指定视频末尾淡出效果的持续时间（以秒为单位）。

### `--timing <deterministic|realtime>`

选择视频播放的时序控制方式。
`deterministic` 保证恒定的采样间隔，而 `realtime` 则真实还原实际时间流逝。

### `--frame <index>`

从多帧录制数据中仅提取指定索引的单帧并输出为静态图。

### `--time <sec>`

提取指定的经历时间点（例如：`2.5`）或开始/结束区间（例如：`1.0-4.0`）并输出。

### `--save-frames <dir>`

将视频生成过程中产生的所有静态 SVG 帧按顺序编号保存至指定目录。

## 录制与重放

### `--save-cast <path>`

将执行过程中的输出保存为终端录制标准格式 asciicast v2 文件。

### `--in <path>`

读取现有的 asciicast v2 录制文件进行渲染，而不是执行新命令。

### `--replay-save <path>`

将命令执行过程中人工输入的键盘操作记录为可供后续重放的文件。

### `--replay <path>`

读取已保存的重放文件，自动重放相同的键盘操作以捕获画面。

### `--timeout <sec>`

以秒为单位指定命令执行的最大等待时间。超过指定时间进程仍未结束时将自动终止。

## 机密保护

### `--mask <pattern>`

针对屏幕上显示的字符串，指定需要隐藏的字符串模式并使用星号进行掩码。可多次指定。

### `--mask-auto [bool]`

设置是否启用基于 QuickLeaks 的机密信息（API 令牌、私钥、个人隐私等）自动检测与掩码（默认值：`true`）。

## 运行环境与高级控制

### `--no-resize`

保持启动时的初始 TTY 尺寸，不通过 console2svg 进行终端尺寸调整。

### `--mouse [bool]`

将交互界面中的鼠标跟踪事件转发给子进程（默认值：`true`），使 TUI 工具能够响应滚轮滚动。

### `--no-colorenv`

停用 PTY 启动时设置的颜色相关环境变量（如 `COLORTERM=truecolor`）的覆盖。

### `--no-delete-envs`

保留 PTY 启动时原本会自动剔除的 CI 相关环境变量（如 `CI` 或 `TF_BUILD`），原样传递给子进程。

### `--coalesce-ms <ms|auto>`

指定将细碎到达的输出事件合并为单帧的时间窗口（以毫秒为单位）。

### `--adjust <spacing|spacingAndGlyphs>`

选择将 SVG 文本长度对齐到单元格宽度时所采用的 `lengthAdjust` 属性方式。

### `--svg-converter <auto|ffmpeg|rsvg-convert|resvg>`

明确指定将 SVG 栅格化为 PNG 或视频格式时使用的转换器引擎。

### `--verbose [path]`

在标准错误中显示详细诊断日志。如果指定了路径，则将日志写入该文件。

## 编程与 AI 智能体集成

### `--json`

将捕获的执行结果以 JSON 格式输出至标准输出。
包含纯文本屏幕内容、退出代码以及生成的图片和预览帧文件路径，便于 AI 智能体和自动化脚本解析结果。

```bash title="Terminal"
console2svg capture --json -- npm test
```

输出的 JSON 包含以下字段：

* `schemaVersion`：JSON Schema 版本
* `status`：执行结果（如 `completed`）
* `exitCode`：执行的子进程退出代码
* `durationMs`：命令总执行时间（毫秒）
* `screen`：终端行列数、纯文本格式的屏幕内容（`text`）以及截断标志（`truncated`）
* `artifacts`：生成的主要输出文件路径与格式
* `frames`：视频录制时生成的代表性预览帧（静态 SVG）文件路径列表（最多 12 帧）

指定 `--json` 时，所有诊断信息都会分离至标准错误，确保标准输出仅包含纯净的 JSON。注意，`--stdout` 与 `--json` 无法同时指定。
