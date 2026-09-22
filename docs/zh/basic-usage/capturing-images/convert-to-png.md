---
title: 转换为 PNG 格式
description: 将拍摄的终端输出自动转换为 PNG 栅格图片的方法，以及转换器的选择。
---

对于不支持 SVG 格式的平台或应用，可以将拍摄结果直接输出为 PNG 图片。

## 以 PNG 格式输出

只需通过 `-o` 选项指定 `.png` 扩展名，就会自动执行栅格化处理。

```bash title="Terminal" "-o output.png"
console2svg capture -o output.png -w 100 -h 12 -- console2svg
```

<!-- c2s::  -w 100 -h 12 -- console2svg -->

## 渲染引擎

console2svg 内置了 [resvg](https://github.com/linebender/resvg)（Rust 编写的高速 SVG 渲染器），无需安装额外工具即可生成高质量 PNG 图片。

如有需要，也可以通过 `--svg-converter` 选项切换转换引擎。

| 设置值 | 说明 |
| :--- | :--- |
| `auto` | 默认。优先使用内置 resvg。 |
| `resvg` | 强制使用内置 resvg。 |
| `rsvg-convert` | 使用系统的 `rsvg-convert` 命令。 |
| `ffmpeg` | 使用 ffmpeg 的 librsvg 解码器。 |

```bash title="Terminal" "--svg-converter rsvg-convert"
console2svg capture -o result.png --svg-converter rsvg-convert -- console2svg
```

<!-- c2s::  -w 100 -h 12 --svg-converter rsvg-convert -- console2svg -->
