---
title: Specify fonts
description: How to specify font families and sizes with the --font and --fontsize options.
---

You can specify the font family and font size used to draw terminal text.

## Specifying a font family

Pass a CSS `font-family` value to the `--font` option.

```bash title="Terminal" "--font"
# Specify a font different from the normal terminal font
console2svg capture --font "Courier New, monospace" -h 10 -- console2svg
```

<!-- c2s:: -o cmd-font.svg -w 100 -h 10 --font "Courier New, monospace" -- console2svg -->

> [!WARNING]
> The viewer's fonts are used, not the fonts on the environment that generated the SVG. Specifying fallback fonts (such as `monospace`) is recommended.

### Default font settings

When `--font` is not specified, the following font settings are used by default.

```css
font-family:
    "JetBrains Mono",
    "Cascadia Mono",
    "Segoe UI Mono",
    "Noto Sans Mono",
    "SFMono-Regular",
    Menlo,
    Consolas,
    "DejaVu Sans Mono",
    "Liberation Mono",
    monospace;
```

> [!TIP]
> [JetBrains Mono](https://www.jetbrains.com/lp/mono/) is the author's favorite font.

## Specifying font size

Use the `--fontsize` option to change the font size in pixels (default: `14`).

```bash title="Terminal" "--fontsize 16"
console2svg capture --fontsize 16 -- cargo test
```
