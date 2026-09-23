---
title: 出力を切り抜く
description: 出力画面の上下左右をピクセル、文字数、またはテキストパターンで切り抜く方法。
---

ビルドログやコマンド出力の一部だけをドキュメントに掲載したい場合、Crop（切り抜き）機能を使用することで、必要な領域だけを切り出してSVG化できます。

## 切り抜きオプション

以下のオプションで各辺の切り抜き量を指定します。

| オプション | 説明 |
| :--- | :--- |
| `--crop-top` | 上側を切り抜く |
| `--crop-bottom` | 下側を切り抜く |
| `--crop-left` | 左側を切り抜く |
| `--crop-right` | 右側を切り抜く |

## 指定方法

### ピクセル (px) または文字数 (ch)

数値をピクセル（`px`）または文字数・行数（`ch`）で直接指定します。

```bash title="Terminal" "--crop-top 20px" "--crop-bottom 3ch" "--crop-left 10px" "--crop-right 10ch"
# 切り抜き前
console2svg capture -w 80 -h 12 -- console2svg

# 上側を20px、下側を3行、左を10px、右を10文字分切り抜く
console2svg capture -w 80 -h 12 \ 
  --crop-top 20px --crop-bottom 3ch \
  --crop-left 10px --crop-right 10ch -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-top 20px --crop-bottom 3ch --crop-left 10px --crop-right 10ch -- console2svg -->

### テキストパターンによる切り抜き

特定の文字列が現れる位置を基準にして動的に切り抜くこともできます。

```bash title="Terminal" "--crop-bottom"
# "Options" という文字列が現れた行より下をすべて切り抜く
console2svg capture -w 80 -h 12 --crop-bottom "Options" -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-bottom "Options" -- console2svg -->

オフセット行数を指定することで、一致した行の前後の行から切り抜くことも可能です。

```bash title="Terminal" "--crop-bottom"
# "Options" の2行前（-2）までを残す
console2svg capture -w 80 -h 12 --crop-bottom "Options::-2" -- console2svg
```

<!-- c2s::  -w 80 -h 12 --crop-bottom "Options::-2" -- console2svg -->
