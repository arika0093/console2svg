---
title: LLM によるターミナル操作
description: バックグラウンドのセッションデーモンを介した、LLM による対話的ターミナル操作と視覚フィードバックループ。
since: v0.11
---

Web ブラウザの自動化において Playwright が果たす役割と同様に、LLM が TUI（テキストユーザーインターフェース）を操作するには、画面の視覚状態を確認しながらキーを送り、その変化を観測するフィードバックループが必要です。
通常の CLI 実行ではプロセスが描画完了と同時に終了するため、vim や fzf のような状態を持つ対話型アプリケーションを操作できません。

`console2svg session` は、バックグラウンドのデーモンプロセスが擬似端末（PTY）の対話セッションを保持し、LLM が JSON 経由で段階的に操作を進めるインターフェースを提供します。
LLM は画面のテキスト配置や SVG キャプチャを観測しながら、TUI のレイアウト調整や動作確認を自律的に進められます。

## エージェント向けの前提設定

LLM にターミナル操作手順を指示するには、エージェント向け指示書である [SKILL.md](./use-skill.md) をシステム指示へ読み込ませます。
エージェントは指示書に従い、後述する JSON ベースのサブコマンドを実行して作業を進めます。

すべての `console2svg session` サブコマンドは、結果を標準出力へ JSON 形式で出力します。
エージェントがテキストの正規表現解析に頼ることなく、終了ステータスや画面状態を構造化データとして取得できるようにするためです。
失敗時の分岐には `error.code` を使用します。
`error.message` や標準エラー出力の診断文は人間向けであり、文面が変更される場合があります。

## 対話操作の基本フロー

LLM がセッションを操作する手順は、以下の流れで構成されます。

1. **開始（start）**：バックグラウンドで PTY セッションを起動します。
2. **寸法調整（resize）**：目的に応じて画面の幅と高さを設定します。
3. **検査と観測（read / inspect）**：現在の画面状態を探索的に確認します。
4. **条件待機と入力送信（wait / send）**：画面の変化を待ち、キー入力を送信します。
5. **成果物保存（capture）**：必要な画面を恒久的な SVG 画像として保存します。
6. **シナリオへのエクスポート（export）**：成功した操作手順をシナリオファイルとして書き出します。
7. **破棄（stop）**：不要になったセッションを明示的に終了します。

### 1. セッションの開始

操作対象のコマンドを指定してバックグラウンドセッションを起動します。

```bash title="Terminal"
console2svg session start -- bash
```

実行に成功すると、セッションを一意に識別する `sessionId` を含む JSON が返されます。
以降の操作では、この ID を引数に指定します。

```json title="出力例"
{
  "sessionId": "s_a1b2c3d4e5f6",
  "state": "running",
  "command": "bash",
  "width": 80,
  "height": 24,
  "createdAt": "2025-01-15T10:00:00Z"
}
```

### 2. 画面寸法の調整

TUI アプリケーションは、端末の幅や高さに応じてレイアウトを変更します。
画面サイズを変更する場合は `session resize` を実行します。

```bash title="Terminal"
console2svg session resize s_a1b2c3d4e5f6 --width 120 --height 30
```

### 3. 画面の検査と観測（read / inspect）

画面の現在状態を把握するため、テキストの取得や一時的な画像確認を行います。

```bash title="Terminal"
# 現在の画面テキストを取得
console2svg session read s_a1b2c3d4e5f6

# セル単位の属性や全角文字情報を含む詳細テキストを取得
console2svg session read s_a1b2c3d4e5f6 --structured

# 一時的な目視確認用 SVG を生成（出力パスは自動割り当て）
console2svg session inspect s_a1b2c3d4e5f6
```

#### inspect と read の位置づけ

`session read` と `session inspect` は、エージェントが画面状況を把握するための **観測**（Observation）です。
これらは状況把握や診断を目的とした **単なる検査用** の操作であり、恒久的な操作手順（Action）ではありません。

* `session read` は画面テキスト、カーソル位置、代替画面の有無などを即座に返します。
* `session inspect` は、マルチモーダルモデルによる目視検査のために、安全な一時ディレクトリ配下へ使い捨ての SVG 画像を出力し、そのファイルパスを返します。
  利用者が保存先ファイルパスを指定する必要はありません。
* `read` と `inspect` は検査用の操作であるため、後述のシナリオエクスポート対象から自動的に除外されます。

#### エージェント向け設計原則：観測から条件への昇華

エージェントが自律的に操作を進める際、重要な原則があります。
`read` や `inspect` で得た情報に基づいて次の入力を決定する場合は、その判断根拠となった画面状態を `session wait` などの **条件**（Condition）として明示的に指定してから次の操作を実行します。

例えば、`read` で画面上に `Overwrite? [y/N]` が表示されたことを確認した直後に、いきなり `send --text "y"` を送信してはいけません。
その場合は以下のように、条件待機を実行してから入力を送信します。

```bash title="Terminal"
# 1. read で画面を検査（Observation）
console2svg session read s_a1b2c3d4e5f6

# 2. 検査結果に基づき、依存関係を条件として明示（Condition）
console2svg session wait s_a1b2c3d4e5f6 --text "Overwrite? [y/N]"

# 3. 操作を実行（Action）
console2svg session send s_a1b2c3d4e5f6 --text "y" --keys Enter
```

このように依存関係を明示することで、セッションエクスポート時に確実で再現性の高い自動化シナリオを生成できます。

### 4. 条件待機と入力送信（wait / send）

画面が期待する状態へ遷移するのを待機し、次のキーシーケンスを端末へ送信します。

#### 条件待機（wait）

画面に指定文字列が出現する、または一度表示された文字列が消えるまで待機します。

```bash title="Terminal"
# 指定文字列の出現を待機
console2svg session wait s_a1b2c3d4e5f6 --text "Ready"

# 処理中表示の消失と、2 秒間の表示安定を待機
console2svg session wait s_a1b2c3d4e5f6 --text "Working" --until absent --stable-for 2s
```

#### キー入力の送信（send）

画面状態を確認したら、端末へキー操作を送信します。

```bash title="Terminal"
# 文字列の送信
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# Enter や矢印キーなどの特殊キーを送信
console2svg session send s_a1b2c3d4e5f6 --keys Enter

# 複数種類の入力を指定順序で連続送信
console2svg session send s_a1b2c3d4e5f6 --text "i" --keys Enter --paste "hello" --keys Esc
```

`--text`、`--paste`、`--keys`、`--raw-hex` は複数回指定でき、指定した順序で一括送信されます。

### 5. 成果物としての画面保存（capture）

一時的な目視確認を行う `inspect` とは異なり、成果物として残したい画像は `session capture` で保存します。

```bash title="Terminal"
console2svg session capture s_a1b2c3d4e5f6 -o docs/assets/status.svg -d macos -t dracula
```

`capture` は恒久的な画像を出力する **操作**（Action）として扱われるため、シナリオエクスポートの対象に含まれます。

### 6. シナリオへのエクスポート（export）

エージェントが対話的に進めて成功した一連の操作パスは、`session export` によって **シナリオファイル**（ScenarioDocument）として出力できます。

```bash title="Terminal"
console2svg session export s_a1b2c3d4e5f6 -o tests/scenarios/setup.yaml
```

エクスポート機能は、セッション履歴から `send`（入力）、`wait`（待機条件）、`resize`、`capture` を抽出して YAML 形式のシナリオを生成します。
単なる検査用である `read` や `inspect` はエクスポート対象から自動的に除外されるため、余分な一時処理を含まない簡潔なシナリオが得られます。

生成されたシナリオファイルは、`console2svg scenario run` コマンドでそのまま再実行できます。
エージェントが対話的に探索・確立した操作手順をシナリオとして保存しておくことで、次回以降は人間や LLM を介さずに CI やリグレッションテストで自動再利用できます。

```bash title="Terminal"
# 出力されたシナリオを再実行
console2svg scenario run tests/scenarios/setup.yaml
```

### 7. セッションの破棄（stop）

一連の作業が完了したら、バックグラウンドのプロセスを明示的に終了します。

```bash title="Terminal"
# 指定セッションを終了
console2svg session stop s_a1b2c3d4e5f6

# 起動中の全セッションを一括終了
console2svg session stop --all --yes
```

セッションを停止すると、バックグラウンドデーモンが PTY を閉じ、関連プロセスと一時ファイルをクリーンアップします。
セッション一覧の確認には `session list`（終了済みも含める場合は `session list --all`）を使用します。
