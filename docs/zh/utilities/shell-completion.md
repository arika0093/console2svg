---
title: Shell 补全
description: 为 bash、zsh、fish 和 PowerShell 安装 Tab 补全脚本。
since: v0.10
---

`completions` 子命令可以输出适用于各种 shell 的补全脚本。

> [!TIP]
> 此补全脚本由 [System.CommandLine.StaticCompletions](https://github.com/dotnet/command-line-api/blob/main/src/System.CommandLine.StaticCompletions/README.md) 生成。

## 各 shell 的安装方法

### Bash

将以下内容添加到 `~/.bashrc`：

```diff lang="bash" title="~/.bashrc"
+ eval "$(console2svg completions script bash)"
```

### Zsh

在 `~/.zshrc` 的 `compinit` 调用之前添加以下内容：

```diff lang="zsh" title="~/.zshrc"
+ eval "$(console2svg completions script zsh)"
```

### Fish

将脚本放入补全目录：

```fish title="Terminal"
mkdir -p ~/.config/fish/completions
console2svg completions script fish > ~/.config/fish/completions/console2svg.fish
```

### PowerShell

将以下内容添加到 PowerShell 配置文件（`$PROFILE`）：

```diff lang="powershell" title="$PROFILE"
+ Invoke-Expression (& console2svg completions script pwsh | Out-String)
```
