---
title: batch
description: Markdownのc2sマーカーから画像を一括生成するコマンド。
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder]
console2svg batch restore <source> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

MarkdownやMDXの`c2s::`マーカーを実行し、直後の画像リンクを生成・更新します。

実際の生成が成功すると、`<output>/assets.json`を自動的かつアトミックに更新します。フィルター実行では、選択されていないMarkdownが所有する既存エントリを保持します。dry runとplaceholder実行はマニフェストを変更しません。

## オプション

### `-i, --input <path>`

Markdownファイルまたはディレクトリを指定します（既定値: `docs`）。

### `-o, --output <dir>`

生成画像の出力先を指定します（既定値: `assets`）。

### `--filter <glob>`

入力ディレクトリからの相対filepathをglobで絞り込みます。`*`はディレクトリ区切りをまたいで一致します。複数指定できます。

### `--dry-run`

ファイルを変更せず、実行予定のジョブだけ表示します。

### `--placeholder`

コマンドを実行せず、不足している空のアセットとMarkdownリンクを作成します。既存アセットは変更しません。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

## `batch restore`

ローカルの`assets.json`またはその格納ディレクトリ、HTTP(S)マニフェストURL、`owner/repository@main/path`形式のGitソースからアセットを復元します。`<source>`と`-o, --output`は必須です。`--filter`、`--force`、`--prune`、`--dry-run`を利用できます。ダウンロードはサイズとSHA-256で検証され、論理パスも再作成されます。`--prune`は以前のマニフェストが管理していた古いパスだけを削除します。

マーカーの詳しい書式は[ドキュメントと画像を同期する](../../automation/document-image-sync.md)を参照してください。
