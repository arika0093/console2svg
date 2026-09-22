---
title: 查看状态
description: 使用 console2svg status 命令检查运行环境和外部工具检测状态。
since: v0.9
---

运行 `status` 子命令可以查看当前版本、操作系统和运行时、SVG 渲染器、可选功能、主题、ANSI 颜色以及输出格式的可用性。外部命令会实际启动以获取版本，因此仅存在于 `PATH` 中并不会使其显示为 `available`。

## 用法

```bash title="Terminal"
console2svg status
```

输出示例（版本、路径和可用性会因环境而异）：

<!-- c2s:: -o cmd-status.svg -w 100 -- console2svg status -->

## 切换输出格式

使用 `--format` 选项更改输出格式。

* `--format table`（默认）：终端表格格式
* `--format markdown`：可粘贴到 GitHub Issue 等位置的 Markdown 格式
* `--format json`（或 `--json`）：供脚本处理的 JSON 格式

```bash title="Terminal" "--format markdown"
console2svg status --format markdown
```
