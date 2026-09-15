---
title: Capturing command output
description: Capture command output as static images or animated SVGs.
---

The `capture` workflow is the usual entry point. It either runs a command in a pseudo-terminal or reads text from standard input, then renders the resulting terminal screen. The output is `output.svg` unless you set `-o`.

## Run a command

Put the command after `--` so its arguments are passed through unchanged:

```bash
console2svg capture -- git log --oneline -5
```

The separator is recommended whenever the captured command has options of its own:

```bash
console2svg capture -- bash -lc 'printf "ready\\n"'
```

## Capture piped output

```bash
my-command | console2svg capture
```

Use pipe mode when the program already produces the text you want and does not need a terminal. Use command mode for programs that inspect terminal size, color support, or whether output is attached to a TTY.

## Static images

Static captures are the default. Set the terminal dimensions and appearance as needed:

```bash
console2svg capture -w 120 -h 30 -c -d macos-pc -- my-command
```

![A static command capture](/assets/cmd.svg)

## Animated SVGs

Use `-v` to preserve the command's visual changes:

```bash
console2svg capture -v --fps 30 --timeout 5 -- cmatrix -ab
```

![An animated command capture](/assets/cmd-sl.svg)

See [converting output formats](/basic-usage/converting-output-formats/) for GIF and MP4 output.

> [!TIP]
> Add `--timeout <seconds>` to a command that does not exit on its own. This is useful for demos such as `cmatrix` and `nyancat`.
