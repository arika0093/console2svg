---
title: theme
description: Command that manages installed themes.
---

```bash title="Terminal"
console2svg theme list
console2svg theme install <source>
console2svg theme remove <id>
console2svg theme update [id]
```

`list` lists themes, `install` adds themes from directories, archives, or URLs, `remove` deletes themes, and `update` updates one theme or all themes.

## Subcommands and arguments

### `list`

List installed themes.

### `--format <table|markdown|json>`

Choose the output format for `list`.

### `install <source>`

Add a theme from a directory, archive, or URL.

### `remove <id>`

Remove the theme with the specified theme ID.

### `update [id]`

Update the specified theme, or update all themes if the theme ID is omitted.
