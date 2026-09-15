---
title: Themes
description: Choose a bundled terminal, chrome, or full appearance theme.
---

Themes are the primary way to control console2svg's appearance. A theme can provide terminal colors, a window chrome, a desktop background, and layout defaults. The `-d` option remains available for compatibility, but it selects a chrome theme; prefer `--theme` when documenting a reusable appearance.

## Use a theme

```bash
console2svg capture --theme tokyo-night -- git status
```

Use more than one `--theme` value when combining a terminal palette with a chrome theme. Later themes override earlier values where they overlap.

```bash
console2svg capture --theme tokyo-night --theme macos-pc -c -- fastfetch
```

## Bundled terminal palettes

| Theme ID | Description |
| --- | --- |
| `dark` | Default dark terminal palette. |
| `light` | Light terminal palette. |
| `dracula` | Dracula-inspired dark palette. |
| `github-dark`, `github-light` | GitHub-inspired terminal palettes. |
| `gruvbox-dark`, `gruvbox-light` | Gruvbox terminal palettes. |
| `matrix` | Green-on-dark Matrix-style palette. |
| `nord` | Nord terminal palette. |
| `one-light` | One Light terminal palette. |
| `solarized-dark`, `solarized-light` | Solarized terminal palettes. |
| `tokyo-night` | Tokyo Night terminal palette. |

## Bundled window chrome themes

| Theme ID | Description |
| --- | --- |
| `none` | Terminal surface without window chrome. |
| `transparent` | Text-only output with a transparent background. |
| `macos`, `windows` | Compact macOS or Windows Terminal-style chrome. |
| `macos-pc`, `windows-pc` | Desktop-style chrome with outer padding and shadows. |

`-d macos-pc` is equivalent to selecting the `macos-pc` chrome theme. Use `-d` for a short one-off command and `--theme` when the appearance is part of a named workflow.

## Full themes and custom themes

`cyberpunk` is a bundled full theme that combines terminal and surrounding appearance settings. List the themes available in the installed version, install a theme from a directory, archive, or URL, and keep the theme ID in your capture command:

```bash
console2svg theme list
console2svg theme install <package-or-source>
```

Custom themes use a manifest containing palette colors and appearance defaults. See the [theme manifest reference](/reference/file-formats-and-embedded-metadata/) for the file format.

<!-- TODO: Generate visual swatches for every bundled terminal, chrome, and full theme. -->
