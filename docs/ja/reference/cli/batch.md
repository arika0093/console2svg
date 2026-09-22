---
title: batch
description: Markdownのc2sマーカーから画像を一括生成するコマンド。
---

## `batch markdown`

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--link-base <path>] [--filter <glob>] [--dry-run] [--placeholder]
```

MarkdownやMDXの`c2s::`マーカーを実行し、直後の画像リンクを生成・更新します。

実際の生成が成功すると、マニフェストファイル`<output>/assets.json`を自動的に更新します。

### `-i, --input <path>`

Markdownファイルまたはディレクトリを指定します（既定値: `docs`）。

### `-o, --output <dir>`

生成画像の出力先を指定します（既定値: `assets`）。

### `--link-base <path>`

Markdownファイルからの相対パスではなく、指定したルート相対の公開URL配下へ画像リンクを挿入します。例えば`--link-base /assets`では、リンクが`/assets/`から始まります。

### `--filter <glob>`

入力ディレクトリからの相対filepathをglobで絞り込みます。
`*`はディレクトリ区切りをまたいで一致します。複数指定できます。

### `--dry-run`

ファイルを変更せず、実行予定のジョブだけ表示します。

### `--placeholder`

コマンドを実行せず、不足している空のアセットとMarkdownリンクを作成します。既存アセットは変更しません。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

## `batch restore`

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

生成済のマニフェストファイルを指定すると、そこから関連するリソースをローカルにダウンロードし配置します。
ドキュメントサイトの公開時に`batch markdown`を実行した際、そのパスを指定することで、ローカル環境に生成結果をコピーすることが可能です。

### `<source>`

`assets.json`の取得先パス。http, git urlなどを指定することができます。

### `-o --output <dir>`

生成画像の複製先を指定します（既定値: `assets`）。

### `--force`

標準ではSHAがローカル/リモートで一致したものは無視しますが、これを指定することで強制的に取得します。

### `--prune`

リモート先に存在せず、ローカルにのみ存在する画像をまとめて削除します。

### `--dry-run`

ファイルを変更せず、実行予定の内容のみ表示します。
