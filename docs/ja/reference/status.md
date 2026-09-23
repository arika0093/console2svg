---
title: ステータス確認
description: console2svg status コマンドによる動作環境・外部ツール検出状態の確認。
since: v0.9
---

`status` サブコマンドを実行することで、実行中のバージョン、OS・ランタイム、SVGレンダラー、任意機能、テーマ、ANSIカラー、および出力形式の利用可否を確認できます。外部コマンドは実際に起動してバージョンを取得するため、単にPATH上に存在するだけでは `available` になりません。

## 実行方法

```bash title="Terminal"
console2svg status
```

出力例（環境によってバージョン、パス、利用可否は変わります）:

<!-- c2s::  -w 100 -- console2svg status -->


## 出力フォーマットの切り替え

`--format` オプションで出力形式を変更できます。

* `--format table` (デフォルト): ターミナル表形式
* `--format markdown`: GitHub Issue等に貼付可能なMarkdown形式
* `--format json` (または `--json`): スクリプト処理向けのJSON形式

```bash title="Terminal" "--format markdown"
console2svg status --format markdown
```
