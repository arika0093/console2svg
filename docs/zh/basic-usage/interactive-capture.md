---
title: 交互式捕获
description: 在运行交互式 Shell 或 TUI 应用时，用功能键手动拍摄。
---

如果要拍摄 Vim 等编辑器操作，或 REPL 中的交互式作业，可以使用交互式捕获，在任意时机进行截图或录制。

## 开始会话

执行 `interactive` 命令。

```bash title="Terminal"
# 使用默认 shell 启动交互模式
console2svg interactive
# 直接启动指定命令
# console2svg interactive -- vim main.rs
```

会话开始后，可以像普通终端一样输入和操作。

![console2svg 交互式会话](/docs/assets/cmd-interactive.svg)

## 快捷键

会话中可使用以下按键执行拍摄。

| 键 | 动作 | 
| :---: | :--- | 
| `F9` | 视频捕获（开始 / 停止） |
| `F10` | 静态图片捕获 |
| `F12` | 视频拍摄期间暂停 |

## 输出目标

默认会以 `output_YYYYMMDD_HHMMSSsss.svg` 格式输出。
可以用 `-o` 选项指定任意文件名（会自动附加时间戳）。

```bash title="Terminal" "-o my_output.svg"
console2svg interactive -o my_output.svg
# -> my_output_20260101_123456789.svg
```

> [!TIP]
> 在 Interactive 执行中，即使用户指定了文件名，也会自动附加时间戳，以便多次输出时优先避免文件名重复。

指定扩展名后，也会自动执行转换处理。

```bash title="Terminal" "-o my_result.mp4"
console2svg interactive -o my_result.mp4
# -> my_result_20260101_123456789.mp4
```

> [!NOTE]
> 指定视频格式扩展名时，`F10` 键的静态图片捕获会被禁用。


## 组合使用样式指定

与 [capture](../basic-usage/capturing-images/overview.mdx) 模式相同，可以指定主题、窗口样式等选项。

```bash title="Terminal" "-d macos-pc" "-t github-dark"
console2svg interactive -d macos-pc -t github-dark
```
