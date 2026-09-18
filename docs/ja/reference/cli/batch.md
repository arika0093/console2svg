---
title: batch markdown
description: Markdownのc2sマーカーから画像を一括生成するコマンド。
---

```bash title="Terminal"
console2svg batch markdown [--input <path>] [--output <dir>] [--filter <glob>] [--dry-run]
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

### `--verbose [path]`

詳細ログを有効にし、必要ならファイルへ保存します。

マーカーの詳しい書式は[ドキュメントと画像を同期する](../../automation/document-image-sync.md)を参照してください。
