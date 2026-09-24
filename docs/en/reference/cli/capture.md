---
title: capture
description: Capture terminal output as SVG.
---

```bash title="Terminal"
console2svg capture [options] -- command [args...]
```

Runs the specified command in a PTY and saves the final screen as an SVG. Add `-v` to create an animated SVG or video format.

Use `--json` when an agent needs the final screen as text as well as a visual
artifact reference:

```bash title="Terminal"
console2svg capture --json -- npm test
console2svg tmux capture --target %1 --json
```

The versioned result includes `schemaVersion`, `status`, `exitCode`,
`durationMs`, `screen` (`width`, `height`, `text`, `truncated`), and the
generated `artifact` path and format. With video output, `frames` contains at
most 12 sampled SVG frame paths for visual inspection. These previews are
created automatically; they do not require `--save-frames`. JSON is written
only to stdout, while diagnostics remain on stderr. `--json` cannot be combined
with `--stdout`.

## Options

### `-o, --out <path>`

Specify the output file. SVG, PNG, GIF, and other formats are selected based on the extension.

### `--format <format>`

Select the output format explicitly: `svg`, `png`, `jpg`, `webp`, `gif`, `mp4`, or `webm`. Overrides the extension of `-o`.

### `--stdout`

Write the SVG to standard output.

### `-m, --mode <image|video>`

Choose the output mode: `image` or `video`.

### `-v, --video`

Output an animated SVG. This is shorthand for `--mode video`.

### `-w, --width <int|adjust>`

Set the terminal width in characters. `adjust` adjusts it to the input.

### `-h, --height <int|adjust>`

Set the terminal height in lines. `adjust` adjusts it to the input.

### `--timeout <sec>`

Stop the recording after the specified number of seconds.

### `--in <path>`

Render an input asciicast v2 file without running a command.

### `--save-cast <path>`

Save the recorded output as an asciicast v2 file.

### `--embed-cast`

Embed the input or recorded asciicast data in the SVG.

### `--embed-logs`

Embed diagnostic logs in the SVG.

### `--embed-replay`

Embed recorded keyboard input in the SVG.

### `--embed-debug`

Embed all diagnostic information in the SVG.

### `--replay-save <path>`

Save keyboard input during command execution to a file for later replay.

### `--replay <path>`

Replay saved keyboard input.

### `--verbose [path]`

Enable verbose logs. If a value is supplied, also save the logs to a file.

### `-c, --with-command`

Display the executed command at the beginning of the output.

### `--header <text>`

Override the command-line heading.

### `--prompt <text>`

Specify the prompt prefix.

### `-d, --window [style]`

Specify the window frame style. If omitted, `macos` is used.

### `--pc-padding <number>`

Override the padding around the desktop-style window.

### `--opacity <number>`

Set the background opacity from `0` to `1`.

### `-t, --theme <id>`

Specify the appearance theme ID. Can be specified multiple times.

### `--forecolor <color>`

Override the terminal text color.

### `--backcolor <color>`

Override the terminal background color.

### `--margin <number>`

Set the window frame margin.

### `--padding <number>`

Set the padding inside the shell.

### `--background <value>`

Specify a desktop background color or image. Specify it twice to create a gradient.

### `--font <family>`

Specify the CSS font family.

### `--fontsize <px>`

Specify the font size in pixels.

### `--mask <pattern>`

Mask the specified strings in the output. Can be specified multiple times.

### `--mask-auto [bool]`

Enable or disable automatic secret masking by Betterleaks. Enabled by default.

### `--frame <index>`

Output the specified frame from a video or multi-frame input as a still image.

### `--time <sec>`

Output a specified time, or a time range in `START-END` format, as a still image.

### `--size <WxH>`

Specify the output size. Accepted forms are `WIDTH`, `WIDTHx*`, `*xHEIGHT`, and `WIDTHxHEIGHT`.

### `--save-frames <dir>`

Save each frame of an animation to the specified directory.

### `--crop-top <value>`

Crop the top by `px`, `ch`, or text position.

### `--crop-right <value>`

Crop the right by `px` or `ch`.

### `--crop-bottom <value>`

Crop the bottom by `px`, `ch`, or text position.

### `--crop-left <value>`

Crop the left by `px` or `ch`.

### `--no-loop`

Do not loop animated SVGs.

### `--fps <number>`

Specify the maximum frame sampling rate.

### `--timing <deterministic|realtime>`

Specify the video timing control mode.

### `--sleep <sec>`

Specify how many seconds to wait after recording ends.

### `--fadeout <sec>`

Specify the video fade-out duration.

### `--coalesce-ms <ms|auto>`

Specify the interval, in milliseconds, for combining nearby output events. `auto` selects it automatically.

### `--no-resize`

Do not resize the terminal; keep the initial TTY size.

### `--mouse`

Forward mouse tracking to the PTY in interactive mode.

### `--no-colorenv`

Do not override the PTY color environment variables.

### `--no-delete-envs`

Do not delete CI environment variables.

### `--adjust <mode>`

Choose how SVG text length is adjusted: `spacing` or `spacingAndGlyphs`.

### `--svg-converter <converter>`

Choose how to rasterize SVG: `auto`, `ffmpeg`, `rsvg-convert`, or `resvg`.

```bash title="Terminal" "-w 100 -h 24 -c"
console2svg capture -w 100 -h 24 -c -- fastfetch
```
