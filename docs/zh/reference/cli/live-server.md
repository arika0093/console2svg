---
title: live-server
description: 实时通过 HTTP 将终端当前画面作为 SVG 图片进行流式传输的命令。
---

```bash title="Terminal"
console2svg live-server [options] [host:port]
```

`live-server` 是一个子命令，用于在伪终端中启动 Shell 或命令，将其屏幕输出实时转换为 SVG，并通过 HTTP 向浏览器进行直播流式传输。
只需在浏览器中打开直播端点（`http://localhost:38473/`），即可无延迟预览终端操作过程。
它非常适合作为浏览器源导入 OBS Studio 等直播推流工具，在 YouTube 直播或技术会议演讲中高清晰度共享终端画面。

## 连接目标与地址指定

省略参数 `[host:port]` 时，服务器默认监听 `127.0.0.1:38473`。
如果希望允许局域网内的其他设备连接，可显式指定主机和端口，如 `0.0.0.0:38473` 或 `localhost:3000`。

```bash title="Terminal"
# 在默认端口（38473）上进行本地推流
console2svg live-server

# 在端口 3000 上进行本地推流
console2svg live-server 127.0.0.1:3000

# 允许外部连接进行推流
console2svg live-server 0.0.0.0:38473
```

## 选项

### 服务器与推流控制

* `--fps <number>`：指定画面更新的最大采样率（用于调节 CPU 负载与网络带宽）。
* `--no-resize`：不随浏览器端缩放调整，保持启动时的初始 TTY 尺寸固定。
* `--mouse [bool]`：将来自浏览器或交互界面的鼠标操作事件转发至 PTY（默认值：`true`）。
* `--save-cast <path>`：同时将推流过程中的全部输出保存为 asciicast v2 录制文件。

### 外观与主题

* `-d, --window [style]`：指定窗口装饰样式（`macos`、`macos-pc`、`windows` 等）。
* `-t, --theme <id>`：指定外观主题的 ID。
* `--forecolor <color>`, `--backcolor <color>`：覆盖前景色或背景色。
* `--background <value>`：指定窗口背面的背景色或图片。
* `--opacity <number>`：指定终端背景的不透明度（`0.0`～`1.0`）。
* `--font <family>`, `--fontsize <px>`：指定字体系列与字体大小。
* `-c, --with-command`：在画面顶部显示执行的命令行。
* `--header <text>`, `--prompt <text>`：覆盖命令行标题文本或提示符符号。
* `--margin <number>`, `--padding <number>`, `--pc-padding <number>`：微调窗口外侧边距、终端内部内边距及桌面边框间距。

### 掩码与环境控制

* `--mask <pattern>`：掩码指定的字符串模式。
* `--mask-auto [bool]`：设置是否启用基于 QuickLeaks 的机密自动检测与掩码（默认值：`true`）。
* `--no-colorenv`：停用颜色相关环境变量的覆盖。
* `--no-delete-envs`：保留 CI 相关的环境变量而不自动剔除。
* `--adjust <mode>`：指定 SVG 文本长度调整方式（`spacing` 或 `spacingAndGlyphs`）。
* `--verbose [path]`：指定详细日志的输出目标。
