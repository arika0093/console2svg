---
title: シェル補完
description: bash、zsh、fish、PowerShell でのタブ補完スクリプトの導入手順。
---

`completions` サブコマンドにより、各種シェル向けの補完スクリプトを出力できます。

> [!TIP]
> この補完スクリプトは [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api/blob/main/src/System.CommandLine.StaticCompletions/README.md) によって生成されます。

## 各シェルでの導入手順

### Bash

`~/.bashrc` に以下を追記します。

```diff lang="bash" title="~/.bashrc"
+ eval "$(console2svg completions script bash)"
```

### Zsh

`~/.zshrc` の `compinit` 呼び出しより前に以下を追記します。

```diff lang="zsh" title="~/.zshrc"
+ eval "$(console2svg completions script zsh)"
```

### Fish

補完ディレクトリへスクリプトを配置します。

```fish title="Terminal"
mkdir -p ~/.config/fish/completions
console2svg completions script fish > ~/.config/fish/completions/console2svg.fish
```

### PowerShell

PowerShell プロファイル（`$PROFILE`）に以下を追記します。

```diff lang="powershell" title="$PROFILE"
+ Invoke-Expression (& console2svg completions script pwsh | Out-String)
```
