---
title: status
description: 実行環境、各種レンダラー、外部ツールの検出状態を表示・診断するコマンド。
---

```bash title="Terminal"
console2svg status [--format table|markdown|json]
console2svg status --json
```

`status` は、現在のシステム環境、SVG レンダラー（resvg、ffmpeg、rsvg-convert）、インストール済みテーマ、および各種機能の利用可否を網羅的に診断して表示するサブコマンドです。
環境依存の外部ツールが正常に機能しているかをテストし、トラブルシューティングや Issue 報告用の環境情報を取得する際に利用します。

## 診断される項目

コマンドを実行すると、以下のカテゴリごとに詳細な検出結果が出力されます。

* **Application**: console2svg のバージョン番号、実行ファイルの絶対パス、Native AOT の適用状態、配布チャネル
* **Platform**: OS 名およびカーネルバージョン、CPU アーキテクチャ、.NET ランタイムバージョン
* **Renderers**: 同梱 resvg、rsvg-convert、ffmpeg の検出可否・バージョン・パス、および MP4 エンコードに使用可能なコーデック（libx264 等）
* **Features**: 動画生成（Video capture）や tmux ペイン連携など、各機能が実行可能かどうかの判定
* **Themes**: 登録されている組み込みテーマおよびカスタムテーマの認識数
* **Colors / Formats**: ターミナルの ANSI カラー対応状況と、出力フォーマット一覧

## オプション

### `--format <table|markdown|json>`

出力フォーマットを指定します。

* `table`（既定値）：端末上で見やすく整形されたカラー表形式で出力します。
* `markdown`：GitHub の Issue や Pull Request に直接貼り付けられる Markdown 形式で出力します。
* `json`：スクリプト処理や CI の診断ステップに適した構造化 JSON 形式で出力します。

### `--json`

`--format json` と同じく、診断結果を JSON 形式で標準出力へ出力します。短縮指定として利用できます。
