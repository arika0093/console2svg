---
title: theme
description: インストール済みテーマを管理するコマンド。
---

```bash title="Terminal"
console2svg theme list
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`list`はテーマ一覧、`install`はディレクトリ・アーカイブ・URLからの追加、`remove`は削除、`update`は1件または全件の更新を行います。

## サブコマンドと引数

### `list`

インストール済みテーマを一覧表示します。

### `--format <table|markdown|json>`

`list`の出力形式を選びます。

### `install <source>`

ディレクトリ、アーカイブ、URLからテーマを追加します。

### `remove <id>`

指定したテーマIDのテーマを削除します。

### `update [id]`

指定したテーマ、またはテーマIDを省略した場合はすべてのテーマを更新します。
