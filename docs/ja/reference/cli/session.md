---
title: session
description: CLI 呼び出しをまたいでバックグラウンドの端末セッションを起動・操作・キャプチャするコマンド。
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session read <id> [--wait <duration>]
console2svg session send <id> (--keys <key> | --text <text>)
console2svg session resize <id> --width <columns> --height <rows>
console2svg session capture <id> [-o <path>] [appearance options]
console2svg session stop <id>
console2svg session stop --all [--yes]
```

`session` は、バックグラウンドで独立して稼働する擬似端末セッションを管理するためのサブコマンド群です。
一度セッションを開始すれば、別の CLI 呼び出しから画面のテキストを読み取ったり、キー入力を送信したり、現在の表示状態を SVG 画像としてキャプチャしたりできます。
対話的な TUI アプリケーション（エディタ、設定メニュー、対話型 CLI など）を AI エージェントや自動化スクリプトからステップ実行・制御する際に威力を発揮します。

なお、**すべての `session` サブコマンドは標準出力へ構造化 JSON を出力します**（JSON 出力を有効化するオプションは不要です）。診断ログは標準エラー出力へ分離されます。

## サブコマンド一覧と操作フロー

### 1. セッションの起動: `start`

コマンドをバックグラウンドワーカーとして起動し、新しい端末セッションを開始します。

```bash title="Terminal"
console2svg session start --width 120 --height 30 -- btop
```

* `--width <columns>`: 端末の横幅（1〜500、既定値: `100`）
* `--height <rows>`: 端末の高さ（1〜500、既定値: `24`）
* `--cwd <path>`: コマンドを実行する作業ディレクトリ

レスポンスには、一意な `sessionId`（例: `s_abc123`）、プロセスのライフサイクル状態（`state`）、OS のプロセス ID、端末サイズが含まれます。

### 2. 画面状態の読み取り: `read`

セッションの現在の画面内容をプレーンテキストで取得します。

```bash title="Terminal"
console2svg session read s_abc123 --wait 2s
```

* `<id>`: 対象のセッション ID
* `--wait <duration>`: 画面に変化が生じるまで待機する時間（例: `500ms`、`2s`。最大 60 秒）

レスポンスの `screen` オブジェクトには、端末の幅・高さ、画面のプレーンテキスト（`text`、最大 200,000 文字）、および切り捨て有無（`truncated`）が含まれます。
待機時間を指定した場合、画面変化を検知するかタイムアウトに達した時点で最新の画面を返却します（タイムアウトはエラーではなく正常応答として `timedOut: true` が返ります）。

### 3. キー入力とテキスト送信: `send`

実行中のプログラムへキーボード入力やテキストを送信します。

```bash title="Terminal"
# 文字列の入力
console2svg session send s_abc123 --text "git status"
# 特殊キーの送信
console2svg session send s_abc123 --keys Enter
# 制御キー（Ctrl+C など）の送信
console2svg session send s_abc123 --keys Ctrl+C
```

* `<id>`: 対象のセッション ID
* `--text <text>`: 改行を付加せず、指定した文字列をそのまま送信します。
* `--keys <key>`: 特殊キーを送信します。対応キー: `Enter`、`Tab`、`Escape`（`Esc`）、`Backspace`、`Delete`、`Up`、`Down`、`Left`、`Right`、`Home`、`End`、`PageUp`、`PageDown`、`Ctrl+A`〜`Ctrl+Z`、または任意の印字可能文字 1 文字。

入力を送信した後は、直ちに `session read` を呼び出すことで、プログラムが反応した後の最新画面を確認できます。

### 4. 画面サイズの変更: `resize`

稼働中の仮想端末ウィンドウのサイズを動的に変更します。

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

子プロセスに対して SIGWINCH（ウィンドウサイズ変更シグナル）が送られ、対応する TUI アプリケーションが画面を再描画します。

### 5. 現在画面のキャプチャ: `capture`

セッションの現在の画面バッファを、高品質な静止画 SVG 画像として保存します。

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`: 出力先 SVG ファイルパス
* 外観オプション: ウィンドウ装飾（`-d`）、テーマ（`-t`）、文字色・背景色、フォント、余白など、`capture` と共通の外観オプションをすべて利用できます。

### 6. セッション一覧の確認: `list`

現在起動しているセッションの一覧を取得します。

```bash title="Terminal"
console2svg session list
```

### 7. セッションの終了: `stop`

セッションを停止し、関連するプロセスツリーを終了させてリソースをクリーンアップします。

```bash title="Terminal"
# 単一セッションの停止
console2svg session stop s_abc123

# すべての管理セッションの一括停止
console2svg session stop --all --yes
```

* `<id>`: 終了するセッション ID
* `--all`: console2svg が管理するすべてのセッションを一括停止します。
* `-y, --yes`: 一括停止時の確認プロンプトを省略します（パイプ実行時や自動化スクリプトでは必須です）。

セッションを停止すると一時データが削除され、以後は `read` や `capture` を行うことができなくなります。
