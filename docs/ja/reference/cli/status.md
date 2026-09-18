---
title: status
description: 実行環境と依存ツールの状態を確認するコマンド。
---

```bash
console2svg status [--format table|markdown|json]
console2svg status --json
```

バージョン、OS、ランタイム、SVGレンダラー、テーマ、ANSIカラーなどの検出状態を表示します。

## オプション

### `--format <table|markdown|json>`

出力形式を`table`、`markdown`、`json`から選びます。

### `--json`

状態をJSON形式で出力します。`--format json`と同じです。
