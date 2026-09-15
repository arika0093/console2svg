---
title: コマンド出力をキャプチャする
description: コマンドをPTY上で実行し、静止画またはアニメーションSVGとして保存します。
---

普段もっともよく使うのが`capture`です。指定したコマンドを疑似端末(PTY)上で実行し、そのターミナル画面を描画します。`-o`を省略した場合の出力先は`output.svg`です。

現在の`capture`には標準入力をキャプチャするpipeモードはありません。`my-command | console2svg capture`のような使い方はできないため、対象コマンドをconsole2svgから直接起動してください。

## コマンドを実行する

対象コマンドは`--`の後ろに置くのがおすすめです。対象コマンド自身のオプションをconsole2svg側に誤解釈されにくくなります。

```bash
console2svg capture -- git log --oneline -5
```

シェル構文を使いたい場合は、シェル自体をキャプチャ対象にします。

```bash
console2svg capture -- bash -lc 'printf "ready\\n"'
```

コマンドを指定せずに`console2svg capture`だけを実行した場合は、入力待ちにはならずヘルプが表示されます。

## SVGを標準出力へ出す

入力をpipeで受け取る機能はありませんが、生成したSVGを標準出力へ渡すことはできます。

```bash
console2svg capture --stdout -- git status > status.svg
```

`--stdout`はSVGを標準出力へ書き出すためのオプションです。

## 静止画

標準では最終状態を静止SVGとして保存します。

```bash
console2svg capture -w 120 -h 30 -c -d macos-pc -- my-command
```

![静止画キャプチャの例](/assets/cmd.svg)

## アニメーションSVG

`-v` (`--video`)を指定すると、端末の変化をアニメーションSVGとして保存できます。

```bash
console2svg capture -v --fps 30 --timeout 5 -- cmatrix -ab
```

![アニメーションキャプチャの例](/assets/cmd-sl.svg)

GIFやMP4へ直接出力する方法は[出力フォーマットの変換](/ja/basic-usage/converting-output-formats/)を参照してください。

> [!TIP]
> `cmatrix`や`nyancat`のように自動終了しないコマンドには`--timeout <秒>`を付けておくと便利です。
