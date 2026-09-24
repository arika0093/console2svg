---
title: interactive
description: 启动交互式 Shell 并通过随时按键操作捕获屏幕的命令。
---

```bash title="Terminal"
console2svg interactive [options]
```

`interactive` 是一个子命令，用于在伪终端中启动交互式 Shell，支持在操作过程中随时按下快捷键就地捕获屏幕快照或录制视频。
这避免了编写文档时频繁启动外部截图工具的繁琐操作，无需中断终端工作流即可截取高质量的 SVG。

## 操作方式与快捷键

执行该命令后，将启动适合当前环境的交互式 Shell。
在操作过程中的任意时刻按下以下快捷键即可记录画面：

* **F9**：将当前画面捕获为静态 SVG 图片。
* **F10**：切换视频录制的开始与停止。
* `exit` 命令或 **Ctrl+D**：结束交互式会话。

## 选项

除静态帧提取（`--frame`、`--time`）与现有录制回放（`--in`、`--replay`）外，`interactive` 支持与 `capture` 相同的主要外观与输出选项。

### 输出与格式

* `-o, --out <path>`：指定输出文件或保存目录的路径。
* `--format <format>`：明确指定输出格式（`svg`、`png`、`gif`、`mp4` 等）。
* `-v, --video`：将默认捕获模式设置为视频录制。
* `--stdout`：将生成结果输出到标准输出。

### 终端尺寸与布局

* `-w, --width <int|adjust>`：以字符数指定终端宽度（默认值：`100`）。
* `-h, --height <int|adjust>`：以文本行数指定终端高度（默认值：`24`）。
* `--size <WxH>`：指定栅格化时的图片尺寸。
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`：以像素或字符单位裁剪画面的上下左右边缘。

### 外观与主题

* `-d, --window [style]`：指定窗口装饰样式（`macos`、`macos-pc`、`windows` 等）。
* `-t, --theme <id>`：指定外观主题的 ID（可多次指定）。
* `--forecolor <color>`, `--backcolor <color>`：覆盖前景色或背景色。
* `--background <value>`：指定窗口背面的背景色或图片（指定两次则形成渐变）。
* `--opacity <number>`：指定终端背景的不透明度（`0.0`～`1.0`）。
* `--font <family>`, `--fontsize <px>`：指定字体系列与字体大小。
* `-c, --with-command`：在画面顶部显示刚刚执行的命令行。

### 录制与掩码

* `--mask <pattern>`：掩码指定的字符串模式。
* `--mask-auto [bool]`：设置是否启用基于 QuickLeaks 的机密自动检测与掩码（默认值：`true`）。
* `--save-cast <path>`：将整个会话的输出保存为 asciicast v2 文件。
* `--fps <number>`：指定视频录制时的最大采样率。
* `--timeout <sec>`：以秒为单位限制会话的最大运行时间。

### 运行环境控制

* `--mouse [bool]`：将鼠标跟踪事件转发给子进程（默认值：`true`）。
* `--no-colorenv`：停用颜色相关环境变量的覆盖。
* `--no-delete-envs`：保留 CI 相关的环境变量而不自动剔除。
* `--svg-converter <converter>`：指定栅格化使用的引擎（`auto`、`resvg`、`ffmpeg`、`rsvg-convert`）。
* `--verbose [path]`：指定详细日志的输出目标。
