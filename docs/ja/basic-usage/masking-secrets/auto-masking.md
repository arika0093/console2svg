---
title: 自動マスキング機能
description: QuickLeaks エンジンによるAPIキーやトークン、パスワードの自動検出と保護機能。
---

console2svg は出力をSVG化する際、テキスト内容を走査して機密情報を自動検出・マスキングする機能を標準で備えています。

## 概要

組み込みのシークレット検出エンジン [QuickLeaks](../../deep-dive/conversion-process/quickleaks.md) により、以下の機密情報が自動的に保護されます。

* GitHub Personal Access Token (`ghp_...`, `github_pat_...`)
* AWS Access Key / Secret Key
* 各種APIトークン（Slack、Stripe、OpenAIなど）
* RSA / SSH 秘密鍵
* 認証情報付きURI（`https://user:password@host/...`）
* Gitのコミッター情報

検出されたテキスト領域は、SVG生成時に塗りつぶし矩形（またはマスク）で覆われ、元の平文がSVGファイル内に残らないように処理されます。

> [!WARNING]
> とはいえ、あまり過信しすぎないようにしてください。あくまで最低限の保護です。

## 具体例

```bash title="Terminal"
cat <<EOF > .env
CURRENT_DIRECTORY=$(pwd)
APP_SECRET_TOKEN=1234567890thankyou
HTTP_PROXY=http://username:superSecurePassword@10.0.0.1:8080
CONNECTION_STRING=Server=localhost;Database=myDataBase;User Id=myUsername;Password=myPassword;
MYSQL_CONNECTION_URL=mysql://username:superSecurePassword@localhost:3306/myDatabase
GIT_USERNAME=hidden_truth_name
GIT_EMAIL_ADDRESS=hidden_truth_name@example.com
COMMON_HASH=$(openssl rand -hex 16)
SECRET_HASH=$(openssl rand -hex 16)
EOF

console2svg capture -w 100 -h 12 -d macos-pc -t github-dark -- cat .env
```

![console2svg capture with automatic secret masking](/docs/assets/cmd-gallery-mask.svg)

## 有効化と無効化

自動マスキングはデフォルトで有効（`--mask-auto true`）です。通常は追加のオプションなしで機能します。

サンプルのダミーキーなどをそのまま表示させたい場合は、`--mask-auto false` を指定します。

```bash title="Terminal" "--mask-auto false"
console2svg capture -w 100 -h 12 -d macos-pc -t github-dark --mask-auto false -- cat .env
```

<!-- c2s:: -w 100 -h 12 -d macos-pc -t github-dark --mask-auto false -- cat ../../../../.env -->
![console2svg capture with automatic masking disabled](../../../assets/cmd-mask-auto.svg)
