---
title: tmux 集成
description: 直接捕获 tmux 中正在使用的窗格或滚动历史记录。
---

使用 `tmux` 子命令，可以直接从运行中的 tmux 会话提取画面或历史记录并转换为 SVG。

## 基本用法

在 tmux 会话中打开另一个窗口或窗格，也可以从外部 shell 运行。

```bash title="Terminal"
console2svg tmux capture -o tmux-current.svg
```

未指定 `--target` 时，会显示选择要捕获窗格的菜单。

样式选项的指定方式与 [capture](../basic-usage/capturing-images/overview.mdx) 模式相同。

```bash title="Terminal" "-d macos-pc" "-t github-dark"
console2svg tmux capture -d macos-pc -t github-dark -o tmux-current.svg
```

## 指定窗格（`--target`）

使用 `--target` 指定目标窗格标识符。

```bash title="Terminal" "--target"
# 指定窗口 0 的窗格 1
console2svg tmux capture -o pane1.svg --target ":0.1"
```

## 获取历史记录（`--history`）

如需同时捕获之前的输出，请用 `--history` 指定获取的行数。

```bash title="Terminal" "--history 100"
# 生成包含之前 100 行历史记录的捕获结果
console2svg tmux capture -o long-log.svg --history 100 
```

不带参数时，会包含所有可获取的历史记录。

```bash title="Terminal" "--history"
console2svg tmux capture -o full-log.svg --history 
```

## `tmux live-server`

也可以针对 tmux 窗格使用 [live-server](./live-server.md) 功能。其参数与 `live-server` 相同。

```bash title="Terminal"
console2svg tmux live-server
```
