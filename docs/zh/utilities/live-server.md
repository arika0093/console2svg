---
title: Live-Server
description: 将运行中的终端画面实时以 SVG 流的形式传送到浏览器。
---

Live-Server 功能会启动本地 HTTP 服务器，并将终端中运行的内容实时渲染为 SVG，显示在浏览器中。

## 启动方式

将要运行的命令传给 `live-server` 子命令。

```bash title="Terminal"
console2svg live-server -- btop
```

默认情况下，命令会在 `127.0.0.1:38473` 启动 HTTP 服务器。

```text
Live server listening on http://127.0.0.1:38473/
```

在浏览器中打开此 URL，即可低延迟、以矢量质量显示终端状态。

![console2svg live-server](/docs/assets/cmd-liveserver.png)

## 选项

* **`--no-resize`**：禁用浏览器窗口大小变化时终端的自动调整。
* 主题和窗口样式选项（`-d`、`-t` 等）也可以照常应用。

还可以将其作为 OBS Studio 等直播软件的浏览器源，以叠加显示高质量终端画面。
