---
title: Automatic masking
description: Automatic detection and protection of API keys, tokens, and passwords by the QuickLeaks engine.
---

When converting output to SVG, console2svg includes a built-in feature that scans text content and automatically detects and masks sensitive information.

## Overview

The built-in secret detection engine [QuickLeaks](../../deep-dive/conversion-process/quickleaks.md) automatically protects the following sensitive information.

* GitHub Personal Access Token (`ghp_...`, `github_pat_...`)
* AWS Access Key / Secret Key
* Various API tokens (Slack, Stripe, OpenAI, etc.)
* RSA / SSH private keys
* URIs with credentials (`******host/...`)
* Git committer information

Detected text areas are covered with filled rectangles (or masks) during SVG generation so that the original plaintext does not remain in the SVG file.

> [!WARNING]
> That said, please do not place too much trust in it. It is only minimum protection.

## Concrete example

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

console2svg capture -w 100 -h 12 -d macos-pc -t github-dark -- cat .env
```

![console2svg capture with automatic secret masking](/docs/assets/cmd-gallery-mask.svg)

## Enabling and disabling

Automatic masking is enabled by default (`--mask-auto true`). Normally it works without any additional options.

If you want to display sample dummy keys as-is, specify `--mask-auto false`.

```bash title="Terminal" "--mask-auto false"
console2svg capture -w 100 -h 12 -d macos-pc -t github-dark --mask-auto false -- cat .env
```

<!-- c2s:: -w 100 -h 12 -d macos-pc -t github-dark --mask-auto false -- cat ../../../../.env -->
![console2svg capture with automatic masking disabled](../../../assets/cmd-mask-auto.svg)
