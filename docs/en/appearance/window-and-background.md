---
title: Specify window frames and backgrounds
description: How to specify macOS-style or Windows-style window decoration (Chrome), background colors, and background images.
---

You can refine screenshot appearance by configuring window decoration (such as title bars) and backgrounds.

## Window frame style

Specify the window frame with the `-d` or `--window <style>` option. If you specify only `-d` without a value, `macos` is applied.

```bash title="Terminal" "-d macos-pc"
# macOS-style frame with a drop shadow
console2svg capture -d macos-pc -- fastfetch
```

![console2svg capture -d macos-pc -- fastfetch](/assets/cmd-window.svg)

### List

Built-in window frames and display examples are collected in [Built-in window frames](./themes/built-in-window-themes.mdx).

## Specifying a background

You can set a background color or background image with the `--background` option.

### Solid-color background

Specify a color code (HEX).

```bash title="Terminal" "--background"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background "#003060" -- dotnet --version
```

<!-- c2s:: -o cmd-bg1.svg -w 64 -h 6 -c -d macos-pc --background "#003060" --opacity 0.85 -- dotnet --version -->


### Gradient

Specify multiple color codes.

```bash title="Terminal" "--background"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background "#004060" "#0080c0" -- dotnet --version
```

<!-- c2s:: -o cmd-bg2.svg -w 64 -h 6 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.85 -- dotnet --version -->


### Image background

Specify an image file path to display it like a desktop background.

```sh title="Terminal" "--background image.png"
console2svg capture -h 10 -c -d macos-pc --opacity 0.85 \
  --background image.png -- dotnet --version
```

<!-- c2s:: -o cmd-bg3.svg -w 64 -h 6 -c -d macos-pc --background ../../assets/image2.png --opacity 0.85 -- dotnet --version -->


## Margins and opacity

* **`--margin <px>`**: Margin outside the window frame.
* **`--padding <px>`**: Padding inside the terminal (between text and frame).
* **`--pc-padding <px>`**: Outside spacing size for `macos-pc` and `windows-pc`.
* **`--opacity <0.0〜1.0>`**: Background opacity (example: `--opacity 0.9`).
