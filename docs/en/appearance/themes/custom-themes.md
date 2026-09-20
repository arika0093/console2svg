---
title: Use custom themes
description: How to install, update, and remove external themes.
since: v0.10
---

In addition to built-in themes, you can use themes created locally or themes published in Git repositories.

## Installing a theme

```bash title="Terminal"
# GitHub repository URL (automatically fetches an archive when Git is unavailable)
console2svg theme install owner/my-theme
console2svg theme install owner/my-theme2@branch/path/to/theme

# Git repository (requires git)
console2svg theme install https://my-git.example/themes/my-theme.git
```

Installed themes can be applied in the same way as built-in themes.

```bash title="Terminal" "-t my-theme"
console2svg capture -t my-theme -- fastfetch
```

## Managing themes

```bash title="Terminal"
# List themes (also shows built-in themes)
console2svg theme list

# Update a specified theme
console2svg theme update my-theme

# Update all installed themes
console2svg theme update

# Uninstall
console2svg theme remove my-theme
```

## Related pages

* [Use built-in themes](./built-in-themes.md)
* [Built-in theme overview](./built-in-theme-list.mdx)
* [Create custom themes](./create-custom-theme.mdx)
