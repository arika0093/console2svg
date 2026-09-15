---
title: 録画・リプレイ・castファイル
description: ターミナル操作を保存して、同じ入力を繰り返し再生します。
---

リプレイ機能を使うと、キーボード入力と描画を分離できます。一度操作を記録しておけば、同じ入力を使ってサイズやテーマ、ウインドウ装飾だけを変えて何度でも撮り直せます。CIでデモ画像を生成するときには特に便利です。

## キーボード入力を保存する

`--replay-save`を付けてコマンドを実行します。

```bash
console2svg capture --replay-save replay.json -- bash
```

保存した入力を再生する場合は`replay`コマンドを使います。リプレイファイルだけではなく、入力先になるコマンドも指定してください。

```bash
console2svg replay replay.json -d macos -v -- bash
```

リプレイファイルはJSONなので、必要なら内容を確認・編集できます。タイミング情報も保存されるため、同じ操作を自動で再現できます。

## castファイル

Asciicast v2互換のcastファイルには、時刻付きのターミナル出力とメタデータが保存されます。キャプチャと同時に保存するには`--save-cast`を使います。

```bash
console2svg capture --save-cast capture.cast -- my-command
```

すでにあるcastファイルを描画する場合は`cast`コマンドを使います。現在のCLIに`convert`サブコマンドはありません。

```bash
console2svg cast capture.cast -o capture.svg
```

ファイル形式や埋め込みメタデータについては[ファイル形式と埋め込みメタデータ](/ja/reference/file-formats-and-embedded-metadata/)を参照してください。

![リプレイで再現したインタラクティブセッション](/assets/cmd-bash-vim.svg)

> [!CAUTION]
> リプレイファイルには入力したコマンド、castファイルにはターミナル出力が含まれます。公開前に必ず内容を確認してください。生成する画像については[機密情報のマスキング](/ja/basic-usage/masking-sensitive-output/)も利用できます。
