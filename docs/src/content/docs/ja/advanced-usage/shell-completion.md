---
title: シェル補完
description: console2svgのコマンドやオプションをTab補完できるようにします。
---

シェル補完を有効にすると、サブコマンドやオプションをすべて覚えなくてもTabキーで候補を表示できます。

現在のCLIでは`completions script`で補完スクリプトを生成します。

```bash
console2svg completions script bash
```

利用しているバージョンで対応しているシェル名は、次のヘルプで確認してください。

```bash
console2svg completions script --help
```

## Bash

補完スクリプトを一般的なユーザー用ディレクトリへ保存する例です。

```bash
mkdir -p ~/.local/share/bash-completion/completions
console2svg completions script bash > ~/.local/share/bash-completion/completions/console2svg
```

環境によっては`bash-completion`パッケージの導入やシェルの再起動が必要です。

## Zsh

`$fpath`に含めるディレクトリへ保存します。

```zsh
mkdir -p ~/.zsh/completions
console2svg completions script zsh > ~/.zsh/completions/_console2svg
```

`~/.zshrc`で`~/.zsh/completions`を`fpath`へ追加し、`compinit`を実行してください。

補完が読み込まれると、`console2svg <Tab>`から`capture`、`replay`、`cast`、`theme`、`status`などを選べるようになります。
