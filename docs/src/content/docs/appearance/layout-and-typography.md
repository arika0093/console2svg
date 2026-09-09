---
title: Layout and typography
description: Control terminal dimensions, spacing, fonts, prompts, and headers.
---

Layout has two layers: the terminal screen, where applications decide how to wrap output, and the surrounding image, where themes add chrome, padding, shadows, and a desktop background. Set the terminal dimensions explicitly whenever the same image is generated on a developer machine and in CI.

## Terminal dimensions

`-w` sets the terminal width in characters and `-h` sets its height in rows. They affect the command being captured as well as the final image. `--size` accepts combined forms such as `120x30`, `120x*`, or `*x30` when that is clearer.

```bash
console2svg capture -w 120 -h 30 -- dotnet --info
console2svg capture --size 120x30 -- dotnet --info
```

Use the smallest size that shows the output you intend to explain. A fixed height is useful for compact examples; omit it when you want the complete scrollback-sized result.

## Space around the terminal

`--padding` controls the space inside the terminal surface. `--margin` is the gap between the terminal surface and its window chrome. `--pc-padding` controls the outer desktop space supplied by a `*-pc` theme.

```bash
console2svg capture -w 120 -h 30 -d macos-pc --padding 12 --margin 24 --pc-padding 32 -- dotnet --info
```

## Fonts, colors, and command labels

Use `--font` to choose a CSS font family and `--fontsize` to set its size in pixels. `--forecolor` and `--backcolor` override terminal colors without replacing the complete theme. `--with-command` (`-c`) adds the invoked command, while `--prompt` and `--header` customize its label.

```bash
console2svg capture -w 100 -h 4 --font 'JetBrains Mono' --fontsize 16 --prompt '[HELLO!] $' --header my-custom-header --forecolor '#00f040' --backcolor '#042515' -- echo hi
```

![A capture with a custom prompt, header, and colors](/assets/cmd-term-custom.svg)

For individual edge cases, `--adjust` controls the SVG text `lengthAdjust` mode. See the [CLI reference](/reference/cli-reference/) for every layout-related option.
