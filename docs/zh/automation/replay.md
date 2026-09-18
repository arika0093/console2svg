---
title: 重放的记录/播放
description: 记录输入按键，并自动复现相同操作进行捕获的重放功能。
---

可以将键盘输入和时序保存到 JSON 文件中，之后完全复现相同操作并重新拍摄。适合定期更新文档图片和在 CI 中自动化使用。

## 记录重放

为 `--replay-save` 选项指定保存目标文件路径后执行。

```bash
# 记录交互式会话
console2svg interactive --replay-save demo.json -- bash
```

执行中的所有按键和时间间隔都会保存到 `demo.json`。

## 播放重放

若要使用保存的重放文件执行捕获，请使用 `replay` 子命令。

```bash
console2svg replay demo.json -- bash
```

![console2svg replay demo.json -- bash](/docs/assets/cmd-bash-vim.svg)

> [!TIP]
> 播放时也可以自由指定主题、视频输出选项等。
