---
title: 日志输出
description: 显示和保存详细日志，并将调试信息嵌入 SVG 文件。
---

本文介绍用于调查和故障排除的日志功能，以及将信息嵌入 SVG 元数据的方法。

## 日志选项

* **`--verbose`**：在当前目录以 `console2svg_yyyyMMddHHmmss.log` 为文件名保存详细执行日志。
* **`--verbose <path>`** / **`--verbose-log <path>`**：将详细日志写入指定文件，并覆盖已有内容。

详细日志不会输出继承的环境变量值。

```bash title="Terminal" "--verbose-log debug.log"
console2svg capture --verbose-log debug.log -o output.svg -- fastfetch
```

## 将调试信息嵌入 SVG

console2svg 可以将各种诊断数据嵌入生成的 SVG 文档的元数据区域。

* **`--embed-logs`**：将运行时日志嵌入 SVG。
* **`--embed-cast`**：嵌入终端事件流（asciicast v2）。
* **`--embed-replay`**：嵌入键盘输入事件。
* **`--embed-debug`**：一次性嵌入以上全部内容（日志、cast 和 replay）。

```bash title="Terminal" "--embed-debug"
console2svg capture --embed-debug -o debug.svg -- my-app
```

生成的 SVG 外观不会改变，但在提交错误报告时只需分享一个 SVG 文件，开发者就能详细检查运行环境日志及输入输出内容。
