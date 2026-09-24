---
title: LLM によるターミナル操作
description: バックグラウンドのセッションデーモンを介した、LLM による対話的ターミナル操作と視覚フィードバックループ。
since: v0.11
---

Web ブラウザの操作自動化において Playwright が果たしている役割と同様に、LLM が TUI（テキストユーザーインターフェース）を操作するには、画面の視覚状態を確認しながらキーを送り、その変化を観測するフィードバックループが必要です。
しかし、通常の CLI 実行ではプロセスが画面描画の完了と同時に終了してしまうため、vim や fzf、対話的インストーラーのような状態を持つアプリケーションを操作できません。

`console2svg session` は、バックグラウンドのデーモンプロセスが擬似端末（PTY）の対話セッションを保持し、LLM が JSON 経由で段階的に操作を進めるための対話インターフェースを提供します。
LLM は画面のテキスト配置や SVG キャプチャを「目」として観測し、TUI のレイアウト調整や動作確認を自律的に繰り返すことができます。

## エージェント向けの前提設定

LLM にターミナル操作手順を指示するには、あらかじめ用意されたエージェント向け指示書である [SKILL.md](./use-skill.md) をプロンプトまたはシステム指示へ読み込ませます。
エージェントは指示書に従い、次に解説する JSON ベースのコマンド群を実行して作業を進めます。

すべての `console2svg session` サブコマンドは、結果を標準出力へ JSON 形式で返します。
LLM がテキストの曖昧な正規表現解析に頼ることなく、終了ステータスや画面状態を構造化データとして確実に取得できるようにするためです。

## 対話操作の基本フロー

LLM がセッションを操作する手順は、開始、寸法設定、画面確認、入力送信、停止の 5 段階で構成されます。

### 1. セッションの開始

操作対象のコマンドを指定してバックグラウンドセッションを起動します。

```bash title="Terminal"
console2svg session start -- bash
```

実行に成功すると、セッションを一意に識別する `sessionId` を含む JSON が返されます。
以降の操作では、この ID を引数として指定します。

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

TUI アプリケーションの多くは、端末の幅や高さに応じてレイアウトを変化させます。
目的に応じて画面サイズを変更する場合は `session resize` を実行します。

```bash title="Terminal"
console2svg session resize s_a1b2c3d4e5f6 --width 120 --height 30
```

### 3. 画面状態の観測

送信したキーに対する反応を確認するため、画面上のテキストまたは SVG 画像を取得します。

```bash title="Terminal"
# 出力文字列の更新を最大 1 秒間待機してテキストを取得
console2svg session read s_a1b2c3d4e5f6 --wait 1s

# 現在の画面状態を SVG 画像として保存
console2svg session capture s_a1b2c3d4e5f6 -o /tmp/current-screen.svg
```

`session read` は画面上の文字列とカーソル座標を返し、`session capture` は色やフォント属性を含む実際の描画結果を保存します。
マルチモーダル対応の LLM であれば、生成された SVG 画像を直接読み込んでレイアウト崩れや配色の違和感を検知できます。
`session list` には起動中・起動処理中のセッションのみが表示されます。プロセス終了済みのセッションは一覧に表示されませんが、保持期間中は ID で読み取りやキャプチャができます。`session stop` で明示的に停止したセッションは削除され、ID からアクセスできなくなります。

### 4. キー入力の送信

画面状態を確認したら、次に行う操作のキーシーケンスを端末へ送信します。

```bash title="Terminal"
# 通常の文字列を送信
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# Enter や矢印キーなどの特殊キーを送信
console2svg session send s_a1b2c3d4e5f6 --keys Enter

# テキスト入力とキー入力を指定順に連続送信
console2svg session send s_a1b2c3d4e5f6 --text "i" --keys Enter --text "hello" --keys Esc
```

`--text` と `--keys` は複数回指定でき、指定した順序で送信されます。
送信後は再び `session read` を呼び出し、期待する画面状態に遷移したかを検証します。

### 5. セッションの破棄

操作が完了したら、バックグラウンドに残ったプロセスを明示的に終了します。

```bash title="Terminal"
# 特定のセッションを停止
console2svg session stop s_a1b2c3d4e5f6

# 起動中の全セッションを一括停止
console2svg session stop --all --yes
```

セッションを停止すると、バックグラウンドデーモンが PTY を閉じ、関連する子プロセス群と一時ソケットファイルをクリーンアップします。
