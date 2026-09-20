---
title: tmux連携
description: tmux で作業中のペインやスクロールバック履歴を直接キャプチャする方法。
since: v0.10
---

`tmux` サブコマンドを使用することで、実行中のtmuxセッションから画面や履歴を直接吸い出してSVG化できます。

## 基本的な使い方

tmux セッション内で別ウィンドウやペインを開くか、外側のシェルから実行します。

```bash title="Terminal"
console2svg tmux capture -o tmux-current.svg
```

`--target`を指定しない場合、どのペインをキャプチャするか選択するメニューが表示されます。

スタイルオプションは [capture](../basic-usage/capturing-images/overview.mdx)モードと同様に指定できます。

```bash title="Terminal" "-d macos-pc" "-t github-dark"
console2svg tmux capture -d macos-pc -t github-dark -o tmux-current.svg
```

## ペインの指定 (`--target`)

`--target` オプションに対象のペイン識別子を指定します。

```bash title="Terminal" "--target"
# ウィンドウ0のペイン1を指定
console2svg tmux capture -o pane1.svg --target ":0.1"
```

## 履歴の取得 (`--history`)

過去のログを含めてキャプチャしたい場合は、`--history` に取得行数を指定します。

```bash title="Terminal" "--history 100"
# 過去100行分の履歴を含めて生成
console2svg tmux capture -o long-log.svg --history 100 
```

引数なしの場合、取得可能な全ての履歴を含めます。

```bash title="Terminal" "--history"
console2svg tmux capture -o full-log.svg --history 
```

## `tmux live-server`

tmuxのペインを対象に、[live-server](./live-server.md)機能を使用することもできます。各種引数は`live-server`と同じです。

```bash title="Terminal"
console2svg tmux live-server
```
