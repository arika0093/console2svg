---
title: 細かい調整を行う
description: 文字詰め（lengthAdjust）の制御や固定サイズ指定、ヘッダー表示のカスタマイズ。
---

SVG出力の細かなレイアウトや表示内容を微調整するためのオプションです。

## SVGテキストの文字詰め (`--adjust`)

ビューアーやフォントレンダリング環境による文字幅のズレを抑制するために、SVGの `lengthAdjust` 属性を制御できます。

* **`spacing`** (デフォルト): 文字間のスペースのみを調整します。グリフの変形を防ぎます。
* **`spacingAndGlyphs`**: 文字自体の幅も伸縮させ、ターミナルセルの位置に厳密に合わせます。

```bash title="Terminal" "--adjust spacingAndGlyphs"
console2svg capture --adjust spacingAndGlyphs -- btop
```

## ヘッダー・プロンプトの調整

`-c`（`--with-command`）によるコマンド表示の内容をカスタマイズできます。

* **`--prompt <text>`**: プロンプト記号を変更します（デフォルト: `$` または `#`）。
* **`--header <text>`**: 実行コマンド行のテキスト全体を指定した文字列に差し替えます。

```bash title="Terminal" "--prompt" "--header"
# プロンプトを ❯ に変更
console2svg capture -c --prompt "❯ " -- echo "Hello"

# ヘッダー全体を指定文字列に差し替え
console2svg capture --header "user@server:~$ ./build.sh" -- ./build.sh
```

<!-- c2s::  -w 100 -h 4 --prompt "[HELLO!] $" --header "my-custom-header" --forecolor "#00f040" --backcolor "#042515" -- echo "hi" -->
