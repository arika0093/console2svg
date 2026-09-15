---
title: CI/CDで使う
description: CI上で再現性のあるターミナルキャプチャを生成します。
---

console2svgは非対話のCI環境でも利用できます。毎回同じ結果を作りたい場合は、端末サイズ、タイムアウト、出力先、必要に応じてリプレイや動画タイミングを明示してください。Runnerごとの端末サイズ差や、終了しないコマンドによるハングを避けやすくなります。

```bash
console2svg capture -w 120 -h 30 --timeout 10 \
  --mask "$SECRET" -o artifacts/console.svg -- ./generate-output.sh
```

## 再現性を上げるポイント

まずconsole2svgのバージョンを固定します。次に`-w`/`-h`、`--timeout`、出力先を明示し、操作そのものも固定したい場合はリプレイファイルを使います。公開する成果物はSVGや変換後の画像・動画だけにし、verboseログやリプレイファイルは内容を確認するまで非公開にしておくのが安全です。

> [!NOTE]
> console2svgは標準で端末のカラー出力を有効にする環境変数を設定し、色を無効化しやすいCI系の環境変数を取り除きます。Runner側の環境をそのまま使いたい場合は`--no-colorenv`や`--no-delete-envs`を指定してください。

## 典型的な流れ

```text
CI runner
  1. console2svgを固定バージョンでインストール
  2. console2svg capture -w 120 -h 30 --timeout 10 \
       -o artifacts/console.svg -- ./generate-output.sh
  3. artifacts/console.svg をactions/upload-artifactで保存
        |
        v
生成画像をレビューしてからdocsやreleaseへ公開
```

GitHub Actionsでのセットアップ例は[GitHub Actions](/ja/automation/github-actions/)を参照してください。
