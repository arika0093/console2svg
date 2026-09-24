---
title: completions
description: シェル補完（オートコンプリート）スクリプトを生成するコマンド。
---

```bash title="Terminal"
console2svg completions generate <shell>
```

`completions` は、各種シェルにおけるコマンドライン自動補完スクリプトを生成するためのサブコマンドです。
サブコマンド名やオプションフラグ、引数の候補をタブキーで素早く補完できるようになります。

## サブコマンドと引数

### `generate <shell>`

指定したシェル向けの補完定義コードを標準出力へ出力します。
引数 `<shell>` には、以下のシェル名を指定できます。

* `bash`
* `zsh`
* `fish`
* `pwsh`（PowerShell）

## 各シェルでの設定例

生成されたスクリプトをシェルの設定ファイルに読み込ませることで、次回以降のシェル起動時から自動補完が有効になります。

### Bash

```bash title="Terminal"
console2svg completions generate bash > ~/.console2svg.bash
echo "source ~/.console2svg.bash" >> ~/.bashrc
```

### Zsh

補完関数ディレクトリ（`fpath`）へスクリプトを配置します。

```bash title="Terminal"
mkdir -p ~/.zsh/completions
console2svg completions generate zsh > ~/.zsh/completions/_console2svg
```

`~/.zshrc` に以下を追記します（`compinit` の呼び出し前）。

```zsh
fpath=(~/.zsh/completions $fpath)
autoload -Uz compinit && compinit
```

### Fish

```bash title="Terminal"
console2svg completions generate fish > ~/.config/fish/completions/console2svg.fish
```

### PowerShell

```powershell title="Terminal"
console2svg completions generate pwsh >> $PROFILE
```
