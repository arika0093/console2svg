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

<!-- TODO: Add tested installation snippets for Bash, Zsh, Fish, and PowerShell. -->
