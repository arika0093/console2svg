---
title: update
description: Command to check for the latest console2svg release and automatically update the binary.
---

```bash title="Terminal"
console2svg update [options]
```

`update` is a subcommand that checks GitHub for the latest release and automatically updates the running console2svg binary to the newest version (self-update).
It incorporates the latest features and bug fixes without needing to manually download and extract archives.

## Command Behavior

Running the command queries the GitHub Releases API to check for the latest version.
When a newer version exists, it automatically downloads the release archive matching your current OS and CPU architecture and replaces the executable.

If console2svg was installed via a package manager such as Homebrew or Scoop, self-update is suppressed by default to maintain package manager integrity, prompting you to update through the package manager instead.

## Options

### `--check`

Checks whether a newer version is available without downloading or applying updates. Use this to compare your current version against the latest release.

```bash title="Terminal"
console2svg update --check
```

### `-y, --yes`

Skips the confirmation prompt (`Do you want to update? [y/N]`) before applying an update and proceeds automatically. Ideal for CI scripts and unattended execution.

### `-f, --force`

Forces binary update and overwrites the executable, ignoring warnings even in environments managed by package managers (such as Homebrew or Scoop).
