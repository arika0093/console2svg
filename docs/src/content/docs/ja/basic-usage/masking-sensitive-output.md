---
title: 機密情報をマスクする
description: パスワードやトークンなど、公開したくない文字列を描画前に隠します。
---

console2svgでは、自動検出と明示的な`--mask`の2つの方法で機密情報を隠せます。

## 自動マスク

`--mask-auto`は標準で有効です。Betterleaksによる検出結果を使い、機密情報と判断された箇所を描画時に隠します。自動検出を無効化したい場合は`--mask-auto false`を指定できます。

## 文字列を明示してマスクする

確実に隠したい値は`--mask`へ渡してください。複数指定できます。

```bash
console2svg capture --mask password token-12345 -- my-command
```

`--mask`はターミナル出力だけでなく、`-c`で表示するコマンドヘッダーにも適用されます。アニメーションでも同じです。

CIでは環境変数の値をそのまま指定できます。

```bash
console2svg capture --mask "$API_TOKEN" -o output.svg -- ./show-environment.sh
```

> [!CAUTION]
> マスキングは「描画結果」を変更する機能です。元のコマンド、リプレイファイル、castファイル、verboseログ、シェル履歴、CIログから機密情報を削除するわけではありません。画像以外の入力・ログ・中間ファイルも機密情報として扱ってください。
