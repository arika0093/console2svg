---
title: リプレイの記録/再生
description: 入力キーストロークを記録し、同一の操作を自動再現してキャプチャするリプレイ機能。
since: v0.9
---

キーボード入力とタイミングをJSONファイルに保存し、後から完全に同一の操作を再現して再撮影できます。ドキュメント画像の定期更新やCIでの自動化に適しています。

## リプレイの記録

`--replay-save` オプションに保存先ファイルパスを指定して実行します。

```bash title="Terminal" "--replay-save demo.json"
# 対話セッションで操作を記録
console2svg interactive --replay-save demo.json -- bash
```

実行中のキーストロークと時間間隔がすべて `demo.json` に保存されます。

## リプレイの再生

保存したリプレイファイルを使用してキャプチャを実行するには、`replay` サブコマンドを使用します。

```bash title="Terminal" "replay demo.json"
console2svg replay demo.json -- bash
```

![console2svg replay demo.json -- bash](/assets/cmd-bash-vim.svg)

> [!TIP]
> 再生時にもテーマや動画出力オプションなどを自由に指定できます。
