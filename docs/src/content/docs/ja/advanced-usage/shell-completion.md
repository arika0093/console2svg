---
title: Shell completion
description: Enable command completion for supported shells.
---

Shell completion makes subcommands and options easier to discover. console2svg uses the .NET `completions` command to generate a script for a supported shell; save that script in the shell's normal completion directory, then reload its configuration.

For example, generate a Bash completion script with:

```bash
console2svg completions generate bash
```

The command supports Bash, Zsh, Fish, and PowerShell. Run `console2svg completions generate --help` to see the shell names accepted by the installed version and the redirection command appropriate to your shell.

## Installation snippets

Generate the script first, then load it from your shell configuration. Replace the target path with your platform's normal completion directory.

```bash
# Bash (~/.bashrc or ~/.bash_profile)
console2svg completions generate bash > ~/.local/share/bash-completion/completions/console2svg
echo 'source ~/.local/share/bash-completion/completions/console2svg' >> ~/.bashrc
```

```zsh
# Zsh (any directory in $fpath, e.g. ~/.zsh/completions)
mkdir -p ~/.zsh/completions
console2svg completions generate zsh > ~/.zsh/completions/_console2svg
echo 'fpath=(~/.zsh/completions $fpath)' >> ~/.zshrc
```

```fish
# Fish
console2svg completions generate fish > ~/.config/fish/completions/console2svg.fish
```

```powershell
# PowerShell ($PROFILE)
console2svg completions generate powershell >> $PROFILE
```

After reloading the shell, `console2svg <Tab>` should complete subcommands such as `capture`, `replay`, `convert`, `theme`, and `status`.
