---
title: レイアウトと文字表示
description: 端末サイズ、余白、フォント、プロンプト、ヘッダーを調整します。
---

console2svgでは、「コマンドから見える端末サイズ」と「生成画像そのもののサイズ」を別々に扱います。ここを混同すると、折り返し位置や最終画像の解像度が意図せず変わるので注意してください。

## 端末の文字幅・行数

`-w`は端末の横幅を文字数で、`-h`は高さを行数で指定します。この値はPTYで実行するコマンドから見える端末サイズにも影響します。

```bash
console2svg capture -w 120 -h 30 -- dotnet --info
```

開発端末とCIで同じ画像を作りたい場合は、`-w`と`-h`を明示しておくのがおすすめです。

## 変換後の画像サイズ

`--size`は端末の文字数ではありません。PNGやGIF、MP4などへ変換するときの出力ピクセル寸法を指定します。`WIDTH`、`WIDTHx*`、`*xHEIGHT`、`WIDTHxHEIGHT`の形式を使えます。

```bash
console2svg capture -w 120 -h 30 -o output.png --size 1280x720 -- dotnet --info
```

SVG自体の端末レイアウトを変えたい場合は`-w`/`-h`を使ってください。

## 端末の周囲に余白を付ける

`--padding`は端末内部の余白、`--margin`は端末とウインドウ装飾の間隔、`--pc-padding`は`*-pc`テーマの外側にあるデスクトップ部分の余白を調整します。

```bash
console2svg capture -w 120 -h 30 -d macos-pc \
  --padding 12 --margin 24 --pc-padding 32 -- dotnet --info
```

## フォント・色・コマンド表示

`--font`でCSSのfont-family、`--fontsize`でフォントサイズをピクセル指定できます。`--forecolor`と`--backcolor`はテーマを丸ごと変えずに端末の前景色・背景色だけを上書きします。

`--with-command` (`-c`)を付けると実行コマンドを画像先頭へ追加し、`--prompt`と`--header`で表示内容を変更できます。

```bash
console2svg capture -w 100 -h 4 --font 'JetBrains Mono' --fontsize 16 \
  --prompt '[HELLO!] $' --header my-custom-header \
  --forecolor '#00f040' --backcolor '#042515' -- echo hi
```

![プロンプト・ヘッダー・色を変更したキャプチャ](/assets/cmd-term-custom.svg)

文字幅の特殊なケースでは`--adjust`でSVGの`lengthAdjust`を変更できます。各オプションの一覧は[CLIリファレンス](/ja/reference/cli-reference/)を参照してください。
