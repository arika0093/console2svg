---
title: 指定字体
description: 使用 --font 和 --fontsize 选项指定字体族和字号的方法。
---

你可以指定用于绘制终端文字的字体族和字体大小。

## 指定字体族

在 `--font` 选项中使用 CSS 的 `font-family` 格式指定。

```bash title="Terminal" "--font"
# 指定不同于常规终端字体的字体
console2svg capture --font "Courier New, monospace" -h 10 -- console2svg
```

<!-- c2s:: -o cmd-font.svg -w 100 -h 10 --font "Courier New, monospace" -- console2svg -->

> [!WARNING]
> 使用的是查看 SVG 一侧的字体，而不是生成 SVG 环境中的字体。建议指定回退字体（例如 `monospace`）。

### 默认字体设置

未指定 `--font` 时，默认使用以下字体设置。

```css
font-family:
    "JetBrains Mono",
    "Cascadia Mono",
    "Segoe UI Mono",
    "Noto Sans Mono",
    "SFMono-Regular",
    Menlo,
    Consolas,
    "DejaVu Sans Mono",
    "Liberation Mono",
    monospace;
```

> [!TIP]
> [JetBrains Mono](https://www.jetbrains.com/lp/mono/) 是作者最喜欢的字体。

## 指定字号

使用 `--fontsize` 选项以像素为单位更改字号（默认：`14`）。

```bash title="Terminal" "--fontsize 16"
console2svg capture --fontsize 16 -- cargo test
```
