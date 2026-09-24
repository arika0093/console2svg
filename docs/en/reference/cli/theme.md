---
title: theme
description: Subcommands to inspect built-in themes, and install, update, or remove custom themes.
---

```bash title="Terminal"
console2svg theme list [--format table|markdown|json]
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`theme` is a suite of subcommands for managing themes that define terminal text colors, background colors, and palette color schemes.
While console2svg bundles numerous built-in themes, these commands allow you to add and manage custom themes from external sources (directories, ZIP archives, or Git repositories).

## Subcommands

### `list`

Displays a list of installed themes.
Built-in themes and user-installed custom themes are displayed separately.

```bash title="Terminal"
console2svg theme list
```

#### `--format <table|markdown|json>`

Specifies list output format. Default is `table` (formatted for terminal display).
Choose `json` when processing themes in scripts, or `markdown` for copying into documentation.

### `install <source>`

Installs a new custom theme.
The `<source>` argument accepts a local directory path containing theme files, a ZIP archive path, or a public theme URL (such as a GitHub repository).

```bash title="Terminal"
console2svg theme install https://github.com/example/my-custom-theme
```

### `remove <id>`

Removes the custom theme with the specified ID from the system.
Note that built-in themes bundled with the binary cannot be removed.

```bash title="Terminal"
console2svg theme remove my-custom-theme
```

### `update [id]`

Updates installed custom themes to their latest versions.
Passing a specific theme ID to `[id]` updates only that theme; omitting the argument updates all installed custom themes in bulk.

```bash title="Terminal"
console2svg theme update
```
