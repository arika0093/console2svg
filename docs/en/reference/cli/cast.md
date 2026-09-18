---
title: cast
description: Render an asciicast v2 file.
---

```bash
console2svg cast <cast> [options]
```

Replays asciicast v2 events as a terminal screen and generates an SVG or animated SVG.

## Options

### `-o, --out <path>`

### `--stdout`

Specify an output file or write the SVG to standard output.

### `-m, --mode <image|video>`

### `-v, --video`

Choose the output mode or output an animated SVG.

### `-w, --width <int|adjust>`

### `-h, --height <int|adjust>`

Specify the terminal width and height. `adjust` adjusts them to the input.

### `--timeout <sec>`

### `--sleep <sec>`

### `--fadeout <sec>`

Specify the processing time, wait time after completion, and video fade-out duration.

### `--verbose [path]`

Enable verbose logs and optionally save them to a file.

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

Specify whether to display the command, the heading, and the prompt.

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

Specify the window frame, desktop-style window padding, and opacity.

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

Specify or override the theme, text color, and terminal background color.

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

Specify margins, padding, and a background color or image.

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

Specify the font, size, and SVG text-length adjustment mode.

### `--mask <pattern>`

### `--mask-auto [bool]`

Configure string masking and automatic secret masking.

### `--frame <index>`

### `--time <sec>`

### `--size <WxH>`

Specify the frame number, time, and output size.

### `--save-frames <dir>`

Save each frame of an animation to the specified directory.

### `--crop-top <value>`

### `--crop-right <value>`

### `--crop-bottom <value>`

### `--crop-left <value>`

Crop the top, right, bottom, and left of the screen using units or text positions.

### `--no-loop`

### `--fps <number>`

### `--timing <deterministic|realtime>`

Configure looping, the maximum sampling rate, and video timing control.

### `--coalesce-ms <ms|auto>`

Specify the interval for combining nearby output events.

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

Configure mouse tracking forwarding, PTY color environment overrides, and CI environment deletion.

### `--svg-converter <converter>`

Specify how to rasterize SVG.
