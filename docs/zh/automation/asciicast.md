---
title: asciicast 文件的记录/播放
description: asciinema 的 asciicast v2 格式保存、播放，以及向 SVG 文件嵌入元数据。
since: v0.9
---

console2svg 可以与终端录制工具 asciinema 常用的 **asciicast v2** 格式互操作。

## 记录 asciicast

通过指定 `--save-cast` 选项，可以将执行输出保存为 asciicast 格式（`.cast`）。

```bash title="Terminal" "--save-cast session.cast"
console2svg capture --save-cast session.cast -- cargo build
```

保存的 `.cast` 文件也可以用普通的 `asciinema play` 等工具播放。

### 向 SVG 嵌入数据 (`--embed-cast`)

指定 `--embed-cast` 选项后，可以把 asciicast 数据嵌入到生成的 SVG 文件内部的元数据区域。

```bash title="Terminal" "--embed-cast"
console2svg capture --embed-cast -o output.svg -- fastfetch
```

## asciicast 的播放和转换

若要从已有 `.cast` 文件渲染 SVG 或视频，请使用 `cast` 子命令。

```bash title="Terminal" "cast session.cast"
# 输出为静态 SVG
console2svg cast session.cast -o session.svg

# 输出为视频（GIF）
console2svg cast session.cast -v -o session.gif -t monokai
```

或者指定 `capture --in`。

```bash title="Terminal" "--in session.cast"
console2svg capture --in session.cast -d macos-pc -o output.svg
```
