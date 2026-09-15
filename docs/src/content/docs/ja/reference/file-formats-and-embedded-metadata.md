---
title: ファイル形式と埋め込みメタデータ
description: replay、cast、テーマ、SVGメタデータの役割を説明します。
---

console2svgでは用途ごとに複数のファイル形式を使います。公開用の画像と、再現・デバッグ用の元データは分けて扱うのがおすすめです。

## SVG出力

SVGはconsole2svgのネイティブ出力です。端末文字、スタイル、ウインドウ装飾、背景、必要に応じてアニメーション情報を含みます。

## replayファイル

replayファイルは、キーボード入力とタイミングを保存するJSONです。`capture --replay-save`で保存し、`replay`コマンドで指定したコマンドへ再生します。

```bash
console2svg capture --replay-save replay.json -- bash
console2svg replay replay.json -- bash
```

## castファイル

castファイルはAsciicast v2互換で、時刻付きのターミナル出力イベントとメタデータを保存します。

```bash
console2svg capture --save-cast capture.cast -- my-command
console2svg cast capture.cast -o output.svg
```

現在のCLIではcastファイルの描画に`cast`を使い、`convert`サブコマンドはありません。

## テーマ

カスタムテーマにはカラーパレットや見た目の初期値を定義したmanifestが含まれます。ディレクトリ、アーカイブ、URLから`theme install`で追加できます。

## SVGへ埋め込める情報

通常のSVGには、replay入力、cast元データ、verboseログは埋め込まれません。必要な場合だけ次のオプションを使います。

```text
--embed-replay  replay入力を埋め込む
--embed-cast    asciicastデータを埋め込む
--embed-logs    verbose診断ログを埋め込む
--embed-debug   上記の埋め込み診断をまとめて有効化する
```

```text
replay.json ─┐
             ├─> capture / replay ─> output.svg
command ─────┘                      └─ <metadata> (任意)

capture.cast ───> cast ────────────> output.svg
                                  └─ <metadata> (任意)
```

> [!CAUTION]
> 埋め込みメタデータはSVGと一緒に配布されます。入力したコマンド、ローカルパス、診断情報などを含む可能性があるため、公開前に内容を確認してください。
