---
title: ウインドウ装飾と背景
description: ウインドウ装飾、背景、透過率、周囲の余白を調整します。
---

ウインドウ装飾は[テーマ](/ja/appearance/themes/)で指定できます。`macos`や`windows`はコンパクトなターミナル風、`macos-pc`や`windows-pc`は影と外側の余白を持つデスクトップ風の表示です。

## 単色・グラデーション背景

`--background`を1つ指定すると単色、2つ指定するとグラデーションになります。`--opacity`では端末背景の不透明度を調整できます。

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc \
  --background '#003060' --opacity 0.85 -- dotnet --version
```

![青い単色背景上のmacOS風ウインドウ](/assets/cmd-bg1.svg)

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc \
  --background '#004060' '#0080c0' --opacity 0.85 -- dotnet --version
```

![青いグラデーション背景上のmacOS風ウインドウ](/assets/cmd-bg2.svg)

## 画像を背景にする

画像ファイルのパスを渡すこともできます。

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc \
  --background image.png --opacity 0.85 -- dotnet --version
```

![画像背景上のmacOS風ウインドウ](/assets/cmd-bg3.svg)

`--margin`はウインドウ装飾と端末の間隔、`--padding`は端末内部、`--pc-padding`は`*-pc`テーマの外側の余白です。端末サイズやフォントも合わせて調整する場合は[レイアウトと文字表示](/ja/appearance/layout-and-typography/)を参照してください。
