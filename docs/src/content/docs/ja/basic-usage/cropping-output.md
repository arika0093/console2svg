---
title: 出力を切り抜く
description: ピクセル、文字単位、またはテキスト位置を基準に出力をCropします。
---

`--crop-*`オプションを使うと、不要な余白や行を生成画像から取り除けます。

## ピクセル・文字単位で切り抜く

```bash
console2svg capture --crop-top 1ch --crop-left 5px \
  --crop-right 30px -- dotnet --info
```

指定できる方向は`--crop-top`、`--crop-right`、`--crop-bottom`、`--crop-left`の4つです。値にはピクセルや文字単位を利用できます。

## テキストを基準に切り抜く

`--crop-top`と`--crop-bottom`にはサイズの代わりに文字列を指定でき、該当行を基準に切り抜けます。

```bash
console2svg capture \
  --crop-top Host \
  --crop-bottom '.NET runtimes installed:-2' \
  -- dotnet --info
```

末尾の数値オフセットを使うと、マッチした行から少し位置をずらせます。環境によって出力行数が変わるコマンドを切り抜くときに便利です。

![テキスト位置を基準に切り抜いたキャプチャ](/assets/cmd-crop-word.svg)
