---
title: replay
description: Replay saved keyboard input.
---

```bash
console2svg replay <replay.json> [options] -- command [args...]
```

Replays an input file saved with `--replay-save` to repeat the same operation.

## Options

`replay` supports the output, terminal display, recording, masking, and diagnostic options available to `capture`, plus an input file.

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

Specify the recording time limit and wait time after recording ends.

### `--save-cast <path>`

Save the replay result as an asciicast v2 file.

### `--embed-cast`

### `--embed-logs`

### `--embed-replay`

### `--embed-debug`

Embed asciicast data, diagnostic logs, keyboard input, or all diagnostic information in the SVG.

### `--replay <path>`

### `--replay-save <path>`

Specify a file containing additional keyboard input to replay or a file in which to save input.

### `--verbose [path]`

Enable verbose logs and optionally save them to a file.

### `-c, --with-command`

### `--header <text>`

### `--prompt <text>`

Specify whether to display the command, the heading, and the prompt.

### `-d, --window [style]`

### `--pc-padding <number>`

### `--opacity <number>`

Specify the window frame, desktop-style window padding, and opacity (from `0` to `1`).

### `-t, --theme <id>`

### `--forecolor <color>`

### `--backcolor <color>`

Specify or override the theme, text color, and terminal background color.

### `--margin <number>`

### `--padding <number>`

### `--background <value>`

Specify window frame and shell interior padding, and a background color or image.

### `--font <family>`

### `--fontsize <px>`

### `--adjust <mode>`

Specify the font, size, and SVG text-length adjustment mode.

### `--mask <pattern>`

### `--mask-auto [bool]`

Configure string masking and automatic secret masking.

### `--frame <index>`

### `--time <sec>`

Specify the frame number or time (a `START-END` range is also accepted) to output as a still image.

### `--size <WxH>`

Specify the output size as `WIDTH`, `WIDTHx*`, `*xHEIGHT`, or `WIDTHxHEIGHT`.

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

### `--fadeout <sec>`

### `--coalesce-ms <ms|auto>`

Specify the fade-out duration and interval for combining output events.

### `--mouse`

### `--no-colorenv`

### `--no-delete-envs`

Configure mouse tracking forwarding, PTY color environment overrides, and CI environment deletion.

### `--svg-converter <converter>`

Specify how to rasterize SVG.
