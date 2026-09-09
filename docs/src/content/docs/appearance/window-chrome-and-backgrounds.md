---
title: Window chrome and backgrounds
description: Add a desktop background and tune the space around a themed terminal.
---

Window chrome is supplied by a [theme](/appearance/themes/). Use a compact `macos` or `windows` theme for a terminal-like frame, or the `*-pc` variants for a presentation-ready desktop frame with shadows and outer padding.

## Solid and gradient backgrounds

Use one `--background` value for a solid color, or two values for a gradient. `--opacity` controls how strongly the terminal background covers the desktop background.

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc --background '#003060' --opacity 0.85 -- dotnet --version
```

![A macOS desktop theme on a solid blue background](/assets/cmd-bg1.svg)

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc --background '#004060' '#0080c0' --opacity 0.85 -- dotnet --version
```

![A macOS desktop theme on a blue gradient background](/assets/cmd-bg2.svg)

## Image backgrounds

Pass an image path to use it as the desktop background:

```bash
console2svg capture -w 100 -h 10 -c -d macos-pc --background image.png --opacity 0.85 -- dotnet --version
```

![A macOS desktop theme on an image background](/assets/cmd-bg3.svg)

Use `--margin` for the gap between chrome and shell, `--padding` for the terminal's inner space, and `--pc-padding` for the desktop space around a `*-pc` theme. See [layout and typography](/appearance/layout-and-typography/) when the terminal content itself needs a fixed size or font.
