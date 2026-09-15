---
title: Statusとverboseログ
description: 実行環境や変換バックエンドを確認し、問題の切り分けに使います。
---

`status`を使うと、console2svgが認識している実行環境や変換バックエンドを確認できます。ローカルでは変換できるのにCIでは失敗する、といった場合の最初の確認に便利です。

```bash
console2svg status
console2svg status --json
console2svg status --format markdown
```

`--json`はJSON出力の短縮指定です。`--format`では`json`、`markdown`、`table`を選べます。

表示内容にはconsole2svgのバージョン、OS・アーキテクチャ、利用可能なSVG/画像/動画変換バックエンド、tmuxなどの任意機能、テーマ、利用可能な出力形式などが含まれます。具体的なパスやバージョンは環境によって変わります。

## verboseログ

キャプチャや変換の挙動を詳しく確認したい場合は`--verbose`を使います。値を省略するとverboseログを有効化し、パスを指定するとそのファイルへ保存できます。

```bash
console2svg capture --verbose -- dotnet --info
console2svg capture --verbose logs/capture.log -- dotnet --info
```

再現性の調査などで必要なら、`--embed-logs`を使って診断ログをSVGのメタデータへ埋め込むこともできます。

> [!CAUTION]
> verboseログにはコマンド引数、パス、環境情報などが含まれる可能性があります。デバッグが終わったら不要なログを削除し、公開する場合は必ず内容を確認してください。
