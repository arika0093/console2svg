---
title: completions
description: Command to generate shell autocompletion scripts.
---

```bash title="Terminal"
console2svg completions generate <shell>
```

`completions` is a subcommand for generating command-line autocompletion scripts for various shells.
It enables quick tab completion for subcommand names, option flags, and argument candidates.

## Subcommands and Arguments

### `generate <shell>`

Outputs the completion definition code for the specified shell to standard output.
The `<shell>` argument accepts the following shell names:

* `bash`
* `zsh`
* `fish`
* `pwsh` (PowerShell)

## Configuration Examples by Shell

Loading the generated script into your shell configuration file enables autocompletion for subsequent shell sessions.

### Bash

```bash title="Terminal"
console2svg completions generate bash > ~/.console2svg.bash
echo "source ~/.console2svg.bash" >> ~/.bashrc
```

### Zsh

Place the script in a completion functions directory (`fpath`):

```bash title="Terminal"
mkdir -p ~/.zsh/completions
console2svg completions generate zsh > ~/.zsh/completions/_console2svg
```

Add the following to `~/.zshrc` (before calling `compinit`):

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
