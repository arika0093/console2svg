---
title: batch
description: Markdownのc2sマーカーから画像を一括生成するコマンド。
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run] [--placeholder] [--manifest <path>]
console2svg batch restore --input <path-or-url> --output <dir> [--filter <glob>] [--force] [--prune] [--dry-run]
```

MarkdownやMDXの`c2s::`マーカーを実行し、直後の画像リンクを生成・更新します。

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

### `--manifest <path>`

生成成功後に、SHA-256、サイズ、メディアタイプ、論理エイリアスを含むJSONマニフェストを書き出します。

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

## `batch restore`

ローカルファイルまたはHTTP(S) URLのマニフェストからアセットを復元します。`-i, --input`と`-o, --output`は必須です。`--filter`、`--force`、`--prune`、`--dry-run`を利用できます。ダウンロードはサイズとSHA-256で検証され、論理エイリアスも再作成されます。

マーカーの詳しい書式は[ドキュメントと画像を同期する](../../automation/document-image-sync.md)を参照してください。
