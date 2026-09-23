---
title: フォントを指定する
description: --font および --fontsize オプションを使用したフォントファミリやサイズの指定方法。
---

ターミナルの文字描画に使用するフォントファミリやフォントサイズを指定できます。

## フォントファミリの指定

`--font` オプションにCSSの `font-family` 形式で指定します。

```bash title="Terminal" "--font"
# 通常のターミナルフォントとは異なるフォントを指定
console2svg capture --font "Courier New, monospace" -h 10 -- console2svg
```

<!-- c2s::  -w 100 -h 10 --font "Courier New, monospace" -- console2svg -->

> [!WARNING]
> SVGを生成する環境ではなく、閲覧する側のフォントが使用されます。フォールバックフォント(`monospace` など)の指定を推奨します。

### 標準のフォント設定

`--font` を指定しない場合、以下のフォント設定が既定で使用されます。

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
> [JetBrains Mono](https://www.jetbrains.com/lp/mono/) は作者のお気に入りのフォントです。

## フォントサイズの指定

`--fontsize` オプションでピクセル単位のフォントサイズを変更します（デフォルト: `14`）。

```bash title="Terminal" "--fontsize 16"
console2svg capture --fontsize 16 -- cargo test
```
