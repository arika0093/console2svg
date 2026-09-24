---
title: theme
description: 組み込みテーマの確認、カスタムテーマのインストール・更新・削除を行うコマンド。
---

```bash title="Terminal"
console2svg theme list [--format table|markdown|json]
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`theme` は、端末の文字色や背景色、パレット配色を定義するテーマを管理するためのサブコマンド群です。
console2svg には多数の組み込みテーマが同梱されていますが、本コマンドを利用して外部のテーマファイル（ディレクトリ、ZIP アーカイブ、Git リポジトリ）からカスタムテーマを追加・管理できます。

## サブコマンド一覧

### `list`

インストールされているテーマの一覧を表示します。
組み込みテーマとユーザーが追加したカスタムテーマが分けて表示されます。

```bash title="Terminal"
console2svg theme list
```

#### `--format <table|markdown|json>`

一覧の出力形式を指定します。既定値は `table`（ターミナル表示向けの表形式）です。
スクリプトで一覧を処理したい場合は `json`、ドキュメントに転記したい場合は `markdown` を選択できます。

### `install <source>`

新しいカスタムテーマをインストールします。
引数 `<source>` には、テーマファイルが含まれるローカルディレクトリのパス、ZIP アーカイブのパス、または公開されているテーマの URL（GitHub リポジトリなど）を指定できます。

```bash title="Terminal"
console2svg theme install https://github.com/example/my-custom-theme
```

### `remove <id>`

指定した ID のカスタムテーマをシステムから削除します。
なお、バイナリに同梱されている組み込みテーマは削除できません。

```bash title="Terminal"
console2svg theme remove my-custom-theme
```

### `update [id]`

インストール済みカスタムテーマを最新版へ更新します。
引数 `[id]` に特定のテーマ ID を渡した場合はそのテーマのみを更新し、引数を省略した場合はインストールされているすべてのカスタムテーマを一括で更新します。

```bash title="Terminal"
console2svg theme update
```
