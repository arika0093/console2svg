---
title: live-server
description: 配信实时终端 SVG 的命令。
---

```bash
console2svg live-server [options] [host:port]
```

将 PTY 的当前画面作为 SVG 通过 HTTP 配信。省略监听目标时使用默认值。

## 选项

### `--save-cast <path>`

将取得的输出保存为 asciicast v2 文件。

### `--verbose [path]`

启用详细日志，并在需要时保存到文件。

### `-c, --with-command`

在输出开头显示执行命令。

### `--header <text>`

### `--prompt <text>`

覆盖命令行标题或提示符。

### `-d, --window [style]`

指定窗口边框样式。省略值时使用 `macos`。

### `--pc-padding <number>`

### `--margin <number>`

### `--padding <number>`

指定桌面风格窗口、窗口边框和 Shell 内部的留白。

### `--opacity <number>`

以 `0` 到 `1` 指定背景不透明度。

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

指定或覆盖主题、文字颜色和终端背景色。

### `--background <value>`

指定背景颜色或图片。指定两次时会变成渐变。

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

指定字体、大小和 SVG 文本长度调整方法。

### `--mask <pattern>`

### `--mask-auto [bool]`

设置字符串遮盖和自动机密信息遮盖。

### `--fps <number>`

指定画面更新的最大采样帧率。

### `--no-resize`

维持初始 TTY 尺寸。

### `--mouse`

将鼠标跟踪转发到 PTY。

### `--no-colorenv`

### `--no-delete-envs`

设置是否覆盖颜色相关环境变量、是否删除 CI 环境变量。
