---
title: 自动遮盖功能
description: QuickLeaks 引擎自动检测并保护 API 密钥、令牌和密码的功能。
since: v0.10
---

console2svg 在将输出转换为 SVG 时，内置了扫描文本内容并自动检测、遮盖机密信息的功能。

## 概览

内置的机密检测引擎 [QuickLeaks](../../deep-dive/conversion-process/quickleaks.md) 会自动保护以下机密信息。

* GitHub Personal Access Token（`ghp_...`、`github_pat_...`）
* AWS Access Key / Secret Key
* 各类 API 令牌（Slack、Stripe、OpenAI 等）
* RSA / SSH 私钥
* 带认证信息的 URI（`******host/...`）
* Git 提交者信息

检测到的文本区域会在生成 SVG 时被填充矩形（或遮罩）覆盖，确保原始明文不会留在 SVG 文件中。

> [!WARNING]
> 不过请不要过度信赖它。这只是最低限度的保护。

## 具体示例

```bash title="Terminal"
cat <<EOF > .env
CURRENT_DIRECTORY=$(pwd)
APP_SECRET_TOKEN=1234567890thankyou
HTTP_PROXY=http://user:password@10.0.0.1:8080
CONNECTION_STRING=Server=localhost;Database=myDataBase;User Id=myUsername;Password=myPassword;
MYSQL_CONNECTION_URL=mysql://myUsername:myPassword@localhost:3306/myDatabase
GIT_USERNAME=hidden_truth_name
GIT_EMAIL_ADDRESS=hidden_truth_name@example.com
COMMON_HASH=0123456789abcdef0123456789abcdef
SECRET_HASH=fedcba9876543210fedcba9876543210
EOF

console2svg capture -80 -h 14 -d macos-pc -t github-dark -- cat .env
```

![console2svg 自动遮盖机密信息的捕获结果](/assets/cmd-gallery-mask.svg)

## 启用和禁用

自动遮盖默认启用（`--mask-auto true`）。通常无需额外选项即可工作。

如果想原样显示示例用的虚拟密钥等，请指定 `--mask-auto false`。

```bash title="Terminal" "--mask-auto false"
console2svg capture -80 -h 14 -d macos-pc -t github-dark --mask-auto false -- cat .env
```

<!-- c2s::  -w 80 -h 14 -d macos-pc -t github-dark --mask-auto false -- cat ../../../../.env -->
