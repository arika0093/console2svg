---
title: console2svgの仕組み
description: キャプチャからターミナルエミュレーション、SVG描画までの流れを説明します。
---

console2svgは、コマンドやcastファイルからターミナルイベントを取得し、それをターミナルエミュレータへ流して画面状態を作り、最後にSVGとして描画します。必要なら同じフレーム列からPNG、GIF、MP4、WebMなどへ変換します。

```text
capture / replay ─> PTY recording ─┐
cast file ────────> asciicast read ├─> terminal emulation ─> SVG rendering ─> output.svg
                                      ^                         |
tmux pane ─────────> tmux capture ────┘                         └─> png/gif/mp4/webm
```

以前存在したpipe入力は現在の実装にはありません。`some-command | console2svg capture`ではなく、`console2svg capture -- some-command`のように対象コマンドをPTY上で実行します。

## 入力経路

**Capture**では`console2svg capture -- command`としてコマンドをPTY上で起動します。端末サイズやTTY判定を行う`btop`、`vim`、`cmatrix`のようなアプリも、通常のターミナルに近い状態で動作します。

**Replay**では、保存しておいたキーボード入力を指定したコマンドへ再投入します。

```bash
console2svg replay replay.json -- bash
```

**Cast**ではAsciicast v2ファイルを直接読み込みます。

```bash
console2svg cast capture.cast -o output.svg
```

**tmux**では既存のpaneを取得します。

```bash
console2svg tmux capture --target :0 -o output.svg
```

## ターミナルエミュレーションと描画

取得したANSI/VTシーケンスは、色、カーソル移動、スクロールなどを解釈して画面バッファへ反映されます。その画面にテーマ、ウインドウ装飾、フォント、余白、背景、動画タイミングを適用してSVGを生成します。

関連する実装は主に`src/ConsoleToSvg.Record`、`src/ConsoleToSvg.Core/Terminal`、`src/ConsoleToSvg.Core/Svg`、`src/ConsoleToSvg.Converter`に分かれています。

SVG側の構造は[SVGの形式とスタイル](/ja/concepts/svg-format-and-style/)、リプレイやcastについては[ファイル形式と埋め込みメタデータ](/ja/reference/file-formats-and-embedded-metadata/)を参照してください。
