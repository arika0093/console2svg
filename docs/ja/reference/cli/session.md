---
title: session
description: CLI 呼び出しをまたいでバックグラウンドの端末セッションを起動・操作・キャプチャするコマンド。
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session list --all
console2svg session read <id> [--structured]
console2svg session wait <id> --text <literal> [--until present|absent] [--stable-for <duration>] [--timeout <duration>]
console2svg session send <id> (--keys <key> | --text <text> | --paste <text> | --raw-hex <bytes>)
console2svg session resize <id> --width <columns> --height <rows>
console2svg session capture <id> [-o <path>] [appearance options]
console2svg session inspect <id> [appearance options]
console2svg session export <id> -o <path>
console2svg session stop <id>
console2svg session stop --all [--yes]
```

`session` は、バックグラウンドで独立して稼働する擬似端末セッションを管理するためのサブコマンド群です。
一度セッションを開始すれば、別の CLI 呼び出しから画面のテキストを読み取ったり、キー入力を送信したり、現在の表示状態を SVG 画像としてキャプチャしたりできます。
対話的な TUI アプリケーション（エディタ、設定メニュー、対話型 CLI など）を AI エージェントや自動化スクリプトからステップ実行・制御する際に威力を発揮します。

なお、**すべての `session` サブコマンドは標準出力へ構造化 JSON を出力します**（JSON 出力を有効化するオプションは不要です）。
診断ログは標準エラー出力へ分離されます。

操作に失敗した場合も標準出力に JSON エンベロープを返します（例: `{"schemaVersion":1,"status":"error","error":{"code":"session_not_found","message":"..."}}`）。
分岐には `error.code` を使用します。
`error.message` と標準エラー出力の診断文は人間向けであり、文面が変更される場合があります。
`session wait` は条件結果と画面情報を保ち、失敗時に `status: "error"` と `error.code` を加えます。
`session stop --all` の `failed` にはセッションごとの `{sessionId, code, message}` が格納されます。

安定したエラーコードは `session_not_found`、`session_expired`、`session_exited`、`host_unavailable`、`unsupported_key`、`invalid_condition`、`wait_timeout`、`cancelled` です。
その他のコードは `session_not_started`、`invalid_request`、`invalid_operation`、`permission_denied`、`io_error`、`session_error` です。

ホストは IPC 要求を一度に一つ受け付け、接続を受け付けた順に操作を実行します。
複合 `send` は一つの操作として扱います。
画面読み取りの待機は最大 100ms で要求スロットを返すため、長いポーリングで入力を塞ぎません。
ホスト応答の待機上限は 5 秒で、CLI をキャンセルすると待機もキャンセルされます。

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

セッションの現在の画面内容をプレーンテキストで返します。

```bash title="Terminal"
console2svg session read s_abc123
```

* `<id>`: 対象のセッション ID

レスポンスの `screen` オブジェクトには、端末の幅・高さ、画面のプレーンテキスト（`text`、最大 200,000 文字）、切り捨て有無、0 始まりのカーソル行・列と表示状態、代替画面の状態、スクロールバック行数、`scope: "viewport"` が含まれます。
`--structured` を指定すると、セルごとのスタイル、ハイパーリンク、全角文字情報を含むバージョン付きの行優先スナップショットも返します。

```bash title="Terminal"
console2svg session read s_abc123 --structured
```

`read` はエージェントによる探索や診断を目的とした **観測**（Observation）です。
単なる検査用の操作であり、シナリオエクスポートの対象外となります。
エージェントが `read` で観測した画面テキストに基づいて次の操作を決定する場合、その依存関係を `wait` などの条件として明示してからキー入力を送信します。

### 3. 画面テキストの待機: `wait`

画面に指定した文字列が現れる、または消えるまで待機します。
文字列は大文字・小文字を区別し、部分一致で判定します。

```bash title="Terminal"
console2svg session wait s_abc123 --text "Hi! How can I help?"
console2svg session wait s_abc123 --text "Working" --until absent --stable-for 2s --timeout 3m
```

* `--text <literal>`: 一致させる文字列（必須）
* `--until <present|absent>`: 文字列の出現（既定）または消失を待機
* `--stable-for <duration>`: 条件がこの時間継続した場合に成立（既定: 追加待機なし）
* `--timeout <duration>`: 任意のタイムアウト。
  省略時は条件成立、セッション終了、またはコマンドのキャンセルまで待機します。

`--until absent` では、対象文字列が一度画面に表示された後に消えた場合に成立します。
時間単位は `ms`、`s`、`m`、`h`（例: `500ms`、`2s`、`3m`、`1h`）に対応し、単位を省略すると秒として扱います。
JSON レスポンスには `result`（`matched`、`timeout`、`session-ended`）、`matched`、`timedOut`、最新画面とそのバージョンが含まれます。
条件成立時のみ終了コード 0 となり、タイムアウトまたはセッション終了時は終了コード 1 です。
上限なしの待機は Ctrl+C でキャンセルできます。
`wait` は再現可能な依存関係を定義する **条件**（Condition）であり、シナリオエクスポートの対象に含まれます。

### 4. キー入力とテキスト送信: `send`

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
* `--keys <key>`: 特殊キーを送信します。
  対応キー: `Enter`、`Tab`、`Escape`（`Esc`）、`Backspace`、`Delete`、`Up`、`Down`、`Left`、`Right`、`Home`、`End`、`PageUp`、`PageDown`、`Ctrl+A`〜`Ctrl+Z`、または任意の印字可能文字 1 文字。

`--text`、`--paste`、`--keys`、`--raw-hex` を繰り返し指定した場合、コマンドライン上の順序で一つのホスト要求として送信されます。
別クライアントの入力が途中に割り込むことはありません。
`--paste <text>` は通常入力とは異なり、アプリケーションがブラケット付き貼り付けモードを有効にしている場合、端末の開始・終了マーカーでテキストを囲みます。

`--raw-hex <bytes>` は、16 進数 2 桁ずつで表した空でないバイト列をそのまま送信します（例: `1B5B41` は `ESC [ A`）。
意味キーには `Insert`、`F1`〜`F12`、`Shift+Tab`、`Shift+Up`、`Ctrl+Alt+Left`、`Meta+Home` もあります。
キーパッドの意味キーには `KP0`〜`KP9`、`KPDecimal`、`KPEnter`、`KPAdd`、`KPSubtract`、`KPMultiply`、`KPDivide`、`KPSeparator` があります。
キーパッドと修飾なしのカーソルキーは、アプリケーションが選択した端末モードに応じて符号化されます。
`Alt` と `Meta` は印字可能文字の前に Escape を付与し、修飾された移動キーとファンクションキーは xterm の修飾シーケンスを使用します。

入力を送信した後は、直ちに `session read` を呼び出すことで、プログラムが反応した後の最新画面を確認できます。
`send` は端末状態を変化させる **操作**（Action）であり、シナリオエクスポートの対象に含まれます。

### 5. 画面サイズの変更: `resize`

稼働中の仮想端末ウィンドウのサイズを動的に変更します。

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

子プロセスに対して SIGWINCH（ウィンドウサイズ変更シグナル）が送られ、対応する TUI アプリケーションが画面を再描画します。
`resize` は端末状態を変化させる **操作**（Action）であり、シナリオエクスポートの対象に含まれます。

### 6. 現在画面のキャプチャ: `capture`

セッションの現在の画面バッファを、高品質な静止画 SVG 画像として保存します。

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`: 出力先 SVG ファイルパス
* 外観オプション: ウィンドウ装飾（`-d`）、テーマ（`-t`）、文字色・背景色、フォント、余白など、`capture` と共通の外観オプションをすべて利用できます。

`capture` は恒久的な画像成果物を保存する **操作**（Action）であり、シナリオエクスポートの対象に含まれます。

### 7. 現在画面の目視確認: `inspect`

セッションの現在の画面バッファを、出力先の指定なしで一時 SVG に描画します。

```bash title="Terminal"
console2svg session inspect s_abc123 -d macos -t dracula
```

JSON 応答には生成されたパスがセッション ID とともに含まれます。
`inspect` は `capture` と同じ描画経路を使用するため見た目は同一です。
実行中のセッションと、`capture` が利用できる保持中の終了済みセッションで動作します。
外観オプションは `capture` と共通ですが、`-o`、`--out`、`--format`、`--stdout` は使用できません。

一時ファイルは、ユーザーごとのシステム一時ディレクトリ配下のランダムなディレクトリ（予測不能な `inspect-<guid>` ディレクトリ内の `inspect.svg`）に配置されます。
呼び出し元のエージェントやクライアントが画像を読めるよう、即時削除は行いません。
24 時間より古い検査ファイルは、新しい検査の実行時に自動的に削除されます。

`inspect` は目視確認のための **観測**（Observation）であり、単なる検査用の操作です。
成果物を出力する `capture` とは異なり、探索用であるためシナリオエクスポートの対象外となります。

### 8. セッションのエクスポート: `export`

管理セッション内で行われた一連の操作と待機条件を抽出し、再現可能なシナリオファイル（YAML 形式）として保存します。

```bash title="Terminal"
console2svg session export s_abc123 -o scenario.yaml
```

* `<id>`: 対象のセッション ID
* `-o, --out <path>`: 出力先ファイルパス（拡張子を省略した場合は `.yaml` が自動付与されます）

エクスポート処理は、セッション履歴から `send`、`wait`、`resize`、`capture` などの操作（Action）および待機条件（Condition）を抽出してシナリオを構成します。
単なる検査用である `read` や `inspect` はエクスポート対象から自動的に除外されます。
出力されたシナリオファイルは、`console2svg scenario run <path>` でそのまま再実行でき、CI パイプラインやドキュメント同期での自動再利用が可能です。

### 9. セッション一覧の確認: `list`

既定では起動処理中・実行中のセッションを一覧表示します。
`--all` を指定すると、保持中の終了済み・利用不能セッションも含めます。
保持セッションには、判明している場合 `expiresAt` も表示されます。

```bash title="Terminal"
console2svg session list --all
```

### 10. セッションの終了: `stop`

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

セッションを停止すると一時データが削除され、以後は `read`、`capture`、`inspect`、`export` を行うことができなくなります。
