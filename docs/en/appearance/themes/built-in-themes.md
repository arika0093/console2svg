---
title: Use built-in themes
description: How to apply the themes included with console2svg by default.
since: v0.10
---

console2svg includes built-in themes that are ready to use. Just pass a theme ID to
`-t` or `--theme` to change the terminal color scheme and window appearance.

## Apply a theme

```bash title="Terminal" "-t nord"
console2svg capture -w 100 -h 24 -c -t nord -- console2svg
```

<!-- c2s:: -o cmd-theme.svg -w 100 -h 24 -c -t nord -- console2svg -->

Theme IDs are listed in [Built-in theme overview](./built-in-theme-list.mdx). `theme list` shows both built-in and installed themes together.

```bash title="Terminal" "--format markdown"
console2svg theme list
console2svg theme list --format markdown
```

`-t` can also be used with commands that accept themes, such as `replay` and `interactive`, not only with normal capture.

```bash title="Terminal" "-t nord" "-t cyberpunk-pc"
console2svg replay ./session.json -t nord -- bash
console2svg interactive -t cyberpunk-pc -o capture.svg
```

> [!NOTE]
> [Built-in window frames](./built-in-window-themes.mdx) are also defined as part of the theme system.
> Therefore, specifications such as `-t macos` also work.
