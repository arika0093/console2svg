---
title: batch
description: Markdown 内のマーカーから画像を自動生成し、ドキュメントとアセットを同期するコマンド。
---

`batch` は、ドキュメントサイト（Astro Starlight、Docusaurus、VitePress など）や README の Markdown/MDX ファイルを走査し、本文内のキャプチャ指示マーカーから画像を一括生成・同期するためのサブコマンド群です。
ドキュメント内のスクリーンショットが古くなる問題を防ぎ、CI/CD パイプラインで常に最新の実行結果画像を自動更新できます。

## `batch markdown`

Markdown または MDX ファイル内に記述された `c2s::` マーカーを検出し、指定されたコマンドを実行して直後の画像リンクを生成・更新します。

```bash title="Terminal"
console2svg batch markdown [options]
```

### マーカーの記法

Markdown または MDX のコメント形式で、実行オプションとコマンドラインを記述します。

```markdown
<!-- c2s:: -w 100 -h 10 -c -d macos -- fastfetch -->
<img src="/assets/fastfetch.svg" alt="fastfetch output" />
```

MDX の場合は JSX コメント構文も利用できます。

```mdx
{/* c2s:: -w 100 -c -- git status */}
```

`batch markdown` を実行すると、マーカー直後の画像要素（`<img>` タグまたは `![]()` 記法）のリンク先 URL が生成された画像ファイルへと自動更新されます。
また、出力先ディレクトリにはアセットのマニフェストファイル（`assets.json`）が自動生成され、コマンドや出力ファイルのハッシュ値が記録されます。

### オプション

* `-i, --input <path>`: 対象の Markdown ファイルまたはドキュメントディレクトリのルートパスを指定します（既定値: `docs`）。
* `-o, --output <dir>`: 生成された画像ファイルを保存する出力先ディレクトリを指定します（既定値: `assets`）。
* `--link-base <path>`: Markdown ファイルへ挿入する画像リンクの公開 URL プレフィックスを指定します（例: `--link-base /assets` とすると、Markdown 内のリンクが `/assets/filename.svg` となります）。
* `--filter <glob>`: 入力ディレクトリからの相対パスに対して、処理対象とするファイルを glob パターンで絞り込みます（複数指定可能）。
* `--dry-run`: ファイルの生成や書き換えを行わず、実行予定のタスク一覧のみを表示します。
* `--placeholder`: 実際のコマンド実行を行わず、不足している空のアセットファイルとリンクの挿入だけを行います（既存のアセットは変更しません）。
* `--verbose [path]`: 詳細ログを有効化し、必要に応じてファイルへ保存します。

## `batch restore`

リモート環境や別ブランチで生成済みのマニフェストファイル（`assets.json`）を参照し、対応する画像アセットを一括でローカル環境へダウンロード・復元します。

```bash title="Terminal"
console2svg batch restore <source> --output <dir> [options]
```

CI 環境で `batch markdown` によって生成・公開された画像群を、ローカルの開発環境やデプロイ用ビルド環境へ素早く同期する用途に適しています。

### 引数とオプション

* `<source>`（必須）: マニフェストファイル（`assets.json`）の取得元。ローカルのファイルパスのほか、HTTP/HTTPS の URL や Git リポジトリの URL を指定できます。
* `-o, --output <dir>`（必須）: 画像ファイルを復元・配置するディレクトリを指定します。
* `--filter <glob>`: マニフェスト内のパスに対して、復元対象とするファイルを glob パターンで絞り込みます。
* `--force`: ローカルとリモートでコンテンツハッシュ（SHA）が一致している場合でも、スキップせずに強制的に再取得します。
* `--prune`: マニフェストに記載がなく、ローカルの出力ディレクトリにのみ存在する不要な画像ファイルを一括削除します。
* `--dry-run`: 実際のダウンロードや削除を行わず、実行予定の変更内容のみを表示します。
