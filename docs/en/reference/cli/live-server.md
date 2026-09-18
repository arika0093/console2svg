---
title: live-server
description: Command that serves live terminal SVG.
---

```bash title="Terminal"
console2svg live-server [options] [host:port]
```

Serves the current PTY screen over HTTP as SVG. If the listen destination is omitted, the default value is used.

## Options

### `--save-cast <path>`

Save captured output as an asciicast v2 file.

### `--verbose [path]`

Enable detailed logs and optionally save them to a file.

### `-c, --with-command`

Display the executed command at the beginning of the output.

### `--header <text>`

### `--prompt <text>`

Override the command-line header or prompt.

### `-d, --window [style]`

Specify the window frame style. If the value is omitted, `macos` is used.

### `--pc-padding <number>`

### `--margin <number>`

### `--padding <number>`

Specify spacing for desktop-style windows, window frames, and inside the shell.

### `--opacity <number>`

Specify background opacity from `0` to `1`.

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

Specify or override the theme, text color, and terminal background color.

### `--background <value>`

Specify a background color or image. Specify it twice for a gradient.

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

Specify the font, size, and SVG text length-adjustment method.

### `--mask <pattern>`

### `--mask-auto [bool]`

Configure string masking and automatic secret masking.

### `--fps <number>`

Specify the maximum sampling frame rate for screen updates.

### `--no-resize`

Keep the initial TTY size.

### `--mouse`

Forward mouse tracking to the PTY.

### `--no-colorenv`

### `--no-delete-envs`

Configure whether color-setting environment variables are overridden and CI environment variables are deleted.
