---
title: interactive
description: Record an interactive shell or program.
---

```bash
console2svg interactive [options]
```

Starts an interactive shell and begins or ends screen recording with the recording key.

## Options

`interactive` supports the `capture` options for output, terminal display, recording, masking, and diagnostics. `--in`, `--frame`, `--time`, replay options, and embedding options are unavailable.

### `-o, --out <path>`

Specify the output file.

### `--stdout`

Write the SVG to standard output.

### `-m, --mode <image|video>`

Specify the output mode.

### `-v, --video`

Output an animated SVG.

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

Specify the terminal width in characters and height in lines. `adjust` adjusts them to the input.

### `--timeout <sec>`

Stop recording after the specified number of seconds.

### `--save-cast <path>`

Save the recorded output as an asciicast v2 file.

### `-c, --with-command`

Display the executed command at the beginning of the output.

### `--header <text>`

### `--prompt <text>`

Override the command-line heading or prompt.

### `-d, --window [style]`

Specify the window frame style. If omitted, `macos` is used.

### `--pc-padding <number>`

### `--opacity <number>`

Specify desktop-style window padding and opacity (from `0` to `1`).

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

Override the text or terminal background color.

### `--margin <number>`

### `--padding <number>`

Specify the window frame or shell interior padding.

### `--background <value>`

Specify a background color or image. Specify it twice to create a gradient.

### `--font <family>`

### `--fontsize <px>`

Specify the font family or size.

### `--mask <pattern>`

### `--mask-auto [bool]`

Configure masking of specified strings and automatic secret masking. Automatic masking is enabled by default.

### `--save-frames <dir>`

Save each frame of an animation to the specified directory.

### `--size <WxH>`

Specify the output size as `WIDTH`, `WIDTHx*`, `*xHEIGHT`, or `WIDTHxHEIGHT`.

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

Crop the top and bottom by `px`, `ch`, or text position, and the left and right by `px` or `ch`.

### `--no-loop`

Do not loop animated SVGs.

### `--fps <number>`

### `--timing <deterministic|realtime>`

Specify the maximum sampling rate and video timing control mode.

### `--sleep <sec>`

### `--fadeout <sec>`

Specify the wait time after recording or the video fade-out duration.

### `--coalesce-ms <ms|auto>`

Specify the interval for combining nearby output events.

### `--no-resize`

Keep the initial TTY size.

### `--mouse`

Forward mouse tracking to the PTY.

### `--no-colorenv`

### `--no-delete-envs`

Do not override PTY color environment variables or delete CI environment variables.

### `--adjust <mode>`

Choose how SVG text length is adjusted: `spacing` or `spacingAndGlyphs`.

### `--svg-converter <converter>`

Choose how to rasterize SVG: `auto`, `ffmpeg`, `rsvg-convert`, or `resvg`.

### `--verbose [path]`

Enable verbose logs. If a value is supplied, also save the logs to a file.
