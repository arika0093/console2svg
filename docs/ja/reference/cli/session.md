---
title: session
description: console2svgの呼び出しをまたいでPTYセッションを管理します。
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list [--json]
console2svg session read <id> [--wait <duration>] [--json]
console2svg session send <id> (--keys <key> | --text <text>) [--json]
console2svg session resize <id> --width <columns> --height <rows> [--json]
console2svg session capture <id> [-o <path>] [appearance options] [--json]
console2svg session stop <id> [--json]
console2svg session stop --all [--yes] [--json]
```

managed sessionを使うと、TUIを起動し、現在の画面を読み、入力を送り、別々のCLI呼び出しからサイズ変更や停止ができます。`interactive`、`live-server`、tmuxのセッションとは独立しています。
`start`の端末サイズは既定で100x24、作業ディレクトリは現在のディレクトリです。`--width`と`--height`には1から500を指定でき、`--cwd`で作業ディレクトリを変更できます。

## 起動と画面確認

```bash title="Terminal"
console2svg session start --json -- btop
console2svg session read s_abc123 --wait 1s --json
```

start結果には`sessionId`、ライフサイクルの`state`、プロセスID、端末サイズが含まれます。read結果はcapture JSONと同じ`screen`形式（`width`、`height`、プレーンテキストの`text`、`truncated`）を使います。さらに`state`、取得できる場合は`exitCode`、画面の`version`、`timedOut`を返します。waitのタイムアウトはエラーではありません。テキストは200,000文字までです。
待機時間の上限は60秒です。

## 入力送信とサイズ変更

```bash title="Terminal"
console2svg session send s_abc123 --text "search query"
console2svg session send s_abc123 --keys Enter
console2svg session send s_abc123 --keys Ctrl+C
console2svg session resize s_abc123 --width 120 --height 40
```

`--text`は改行を追加せず、UTF-8文字列をそのまま送信します。`--keys`では`Enter`、`Return`、`Tab`、`Escape`/`Esc`、`Backspace`、`Delete`、`Up`、`Down`、`Left`、`Right`、`Home`、`End`、`PageUp`、`PageDown`、`Ctrl+A`から`Ctrl+Z`、または印字可能な1文字を指定できます。操作後の画面は別途`read`で確認します。

## キャプチャと停止

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg --json
console2svg session stop s_abc123
console2svg session stop --all --yes
```

`session capture`は現在の画面をSVGにし、既存の外観オプションを利用できます。このコマンドの出力形式はSVGのみです。`stop --all`はmanaged sessionだけを対象とし、標準入力がリダイレクトされている場合は`--yes`が必要です。

終了したセッションは24時間保持され、その後削除されます。セッションを停止すると保存ファイルも削除されるため、`session list`に表示されず、readやcaptureもできなくなります。workerに接続できない場合は、最後に保存した画面と`unavailable`状態を返します。`session list`には`session start`で作成され、まだ停止されていないセッションが表示されます。
