---
title: completions
description: 生成 Shell 自动补全脚本的命令。
---

```bash title="Terminal"
console2svg completions generate <shell>
```

`completions` 是一个子命令，用于为各类 Shell 生成命令行自动补全脚本。
它支持使用 Tab 键快速补全子命令名称、选项标志和参数候选值。

## 子命令与参数

### `generate <shell>`

将指定 Shell 的补全定义代码输出到标准输出。
参数 `<shell>` 支持以下 Shell 名称：

* `bash`
* `zsh`
* `fish`
* `pwsh`（PowerShell）

## 各 Shell 配置示例

将生成的脚本加载到 Shell 配置文件中后，下次启动 Shell 起即可启用自动补全。

### Bash

```bash title="Terminal"
console2svg completions generate bash > ~/.console2svg.bash
echo "source ~/.console2svg.bash" >> ~/.bashrc
```

### Zsh

将脚本放置在补全函数目录（`fpath`）中：

```bash title="Terminal"
mkdir -p ~/.zsh/completions
console2svg completions generate zsh > ~/.zsh/completions/_console2svg
```

在 `~/.zshrc` 中追加以下内容（需在调用 `compinit` 之前）：

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
