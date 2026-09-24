---
title: tmux
description: 実行中の tmux ペインを指定し、画面のスナップショット取得やライブ配信を行うコマンド。
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`tmux` は、すでに実行中の **tmux**（端末マルチプレクサ）のペインを対象として、動作中のプロセスを中断することなく、画面状態を直接キャプチャまたはライブ配信するためのサブコマンド群です。
ロングランの学習ジョブやビルドプロセス、あるいは常駐している開発サーバーの画面を、外部からいつでも SVG として撮影・共有できます（Unix 系環境および WSL で利用可能です）。

## サブコマンド

### `tmux capture`

指定した tmux ペインの現在の画面を SVG 画像として記録します。

```bash title="Terminal"
console2svg tmux capture --target %1 -o pane.svg -d macos
```

#### `--target <pane>`（必須）

対象とする tmux ペインの識別子を指定します。
ペイン ID（`%0`、`%1`）、ウィンドウ・ペイン番号（`:0.1`、`session:0.1`）、またはペインタイトルなどを指定できます。

#### `--history [lines]`

画面に見えている領域だけでなく、スクロールバック履歴も含めてキャプチャします。
数値を指定した場合は指定行数分遡って取得し、引数を省略して `--history` のみ指定した場合はバッファ内の全履歴を取得します。

#### `--json`

キャプチャ結果を JSON 形式で標準出力へ出力します。
ペイン内のプレーンテキスト内容、寸法、および生成された画像のファイルパスを取得できます。

```bash title="Terminal"
console2svg tmux capture --target %1 --json
```

レスポンスの構造は [`capture` コマンドの `--json`](./capture.md#json) と同様です。

### `tmux live-server`

指定した tmux ペインの画面をリアルタイムに SVG 化し、ブラウザ向けに HTTP ライブ配信します。

```bash title="Terminal"
console2svg tmux live-server --target :0.0 127.0.0.1:38473
```

#### `--target <pane>`（必須）

配信対象の tmux ペイン識別子を指定します。

#### `[host:port]`

リッスンするアドレスとポート番号を指定します（既定値: `127.0.0.1:38473`）。

## 利用可能な共通オプション

`tmux capture` では [`capture`](./capture.md) の外観・マスキングオプション（`-d`、`-t`、`--margin`、`--font`、`--mask` など）をそのまま利用できます。
同様に、`tmux live-server` では [`live-server`](./live-server.md) の配信・外観オプション（`--fps`、`--mask-auto` など）を利用できます。
