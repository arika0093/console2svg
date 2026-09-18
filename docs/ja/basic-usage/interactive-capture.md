---
title: 対話的キャプチャ
description: 対話型シェルやTUIアプリケーションを実行しながら、ファンクションキーで手動撮影
---

Vimなどのエディタ操作やREPLでの対話作業を撮影する場合、対話的キャプチャ を使用することで、任意のタイミングでスクリーンショットや録画を行えます。

## セッションの開始

`interactive` コマンドを実行します。

```bash
# デフォルトシェルで対話モードを開始
console2svg interactive
# 特定のコマンドを直接起動
# console2svg interactive -- vim main.rs
```

セッションが開始されると、通常のターミナルと同様に入力・操作を行えます。

![console2svg interactive session](/docs/assets/cmd-interactive.svg)

## ショートカットキー

セッション中、以下のキーで撮影を実行できます。

| キー | 動作 | 
| :---: | :--- | 
| `F9` | 動画キャプチャ(開始 / 停止) |
| `F10` | 静止画キャプチャ |
| `F12` | 動画撮影中に一時停止 |

## 出力先

標準では `output_YYYYMMDD_HHMMSSsss.svg` の形式で出力されます。
`-o` オプションで任意のファイル名を指定できます(タイムスタンプは自動付与されます)。

```bash
console2svg interactive -o my_output.svg
# -> my_output_20260101_123456789.svg
```

> [!TIP]
> Interactive実行においては、複数回出力した際もファイル名が重複しないことを優先して、ユーザー指定のファイル名に対しても自動でタイムスタンプが付与されます。

拡張子を指定することで、自動で変換処理も行われます。

```bash
console2svg interactive -o my_result.mp4
# -> my_result_20260101_123456789.mp4
```

> [!NOTE]
> 動画フォーマットの拡張子を指定した場合、`F10`キーでの静止画キャプチャは無効化されます。


## スタイル指定の併用

[capture](../basic-usage/capturing-images/overview.mdx)モードと同様に、テーマやウインドウスタイルなどのオプションを指定できます。

```bash
console2svg interactive -d macos-pc -t github-dark
```
