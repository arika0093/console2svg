---
title: capture
description: Command to execute terminal commands and record output as vector images (SVG) or videos.
---

```bash title="Terminal"
console2svg capture [options] -- command [args...]
```

`capture` is the core subcommand that executes a specified command via a PTY (pseudo-terminal) and saves the exit screen state as an SVG image.
Specifying the `-v` (or `--video`) option records screen transitions during execution as an animated SVG or various video formats (MP4, WebM, GIF).

## Destination and Formats

### `-o, --out <path>`

Specifies the output file path.
Output format is inferred automatically from file extension (`.svg`, `.png`, `.gif`, `.mp4`, `.webm`, etc.). Defaults to `output.svg` if omitted.

### `--format <svg|png|jpg|webp|gif|mp4|webm>`

Explicitly specifies the output format.
This option takes precedence over the file extension specified with `-o`.

### `--stdout`

Writes the generated SVG content directly to standard output instead of saving to a file. Useful when piping to other tools.

### `-m, --mode <image|video>`

Selects the output mode: still image (`image`) or video (`video`).

### `-v, --video`

Outputs as an animated SVG or video. Shorthand for `-m video`.

## Terminal Size and Screen Cropping

### `-w, --width <int|adjust>`

Specifies terminal width in columns (characters). Default is `100`.
Specifying `adjust` automatically fits width to the longest line of output.

### `-h, --height <int|adjust>`

Specifies terminal height in rows (lines of text). Default is `24`.
Specifying `adjust` automatically fits height to the total line count of output.

### `--size <WxH>`

Specifies output pixel dimensions when rasterizing.
Supports width and height such as `1200x800`, as well as aspect-ratio-preserving single-dimension syntax such as `1200x*` or `*x800`.

### `--crop-top <value>`, `--crop-bottom <value>`

Crops the top or bottom edges of the screen by a specified extent.
Crop boundaries can be specified in pixels (`10px`), character cells (`2ch`), or the appearance of a specific string (`"build finished"`).

### `--crop-left <value>`, `--crop-right <value>`

Crops the left or right edges of the screen by a specified extent. Specified in pixels (`10px`) or character cells (`4ch`).

## Appearance and Theme Settings

### `-d, --window [style]`

Adds window decorations (a window frame) around the terminal.
If the style name is omitted, `macos` is applied. Key styles include `macos`, `macos-pc` (with extra margin), `windows`, and `none` (no decoration).

### `-t, --theme <id>`

Specifies the appearance theme ID to apply. Specifying this multiple times layers color palettes and window themes together.

### `--forecolor <color>`, `--backcolor <color>`

Individually overrides default foreground or background color using hex color codes like `#ffffff`.

### `--opacity <number>`

Specifies terminal background opacity between `0.0` (fully transparent) and `1.0` (fully opaque).

### `--background <value>`

Specifies a desktop background color or background image file path placed behind the window.
Specifying two color codes consecutively automatically generates a smooth gradient background.

### `--font <family>`

Specifies the CSS font family used in the SVG (e.g. `JetBrains Mono, monospace`).

### `--fontsize <px>`

Specifies font size in pixels (default: `14`).

### `-c, --with-command`

Displays the executed command line (e.g. `$ npm test`) as a header at the very top of the terminal screen.

### `--header <text>`, `--prompt <text>`

Overrides the entire command header text or prompt symbol (default: `$`) displayed with `-c`.

### `--margin <number>`, `--padding <number>`, `--pc-padding <number>`

Fine-tunes outer window margins, inner terminal shell padding, and desktop frame spacing numerically.

## Animation and Video Controls

### `--fps <number>`

Specifies maximum frame rate (sampling frequency per second) for videos and animated SVGs.

### `--no-loop`

Disables automatic looping in animated SVGs, freezing playback on the final frame upon completion.

### `--sleep <sec>`

Specifies the duration in seconds to hold the final frame after video playback ends, preventing immediate looping back to the start.

### `--fadeout <sec>`

Specifies the duration in seconds for a fadeout effect at the end of the video.

### `--timing <deterministic|realtime>`

Selects the timing control strategy for video playback.
`deterministic` guarantees fixed sampling intervals, while `realtime` faithfully reproduces real-world elapsed time.

### `--frame <index>`

Extracts and outputs only the specified frame index as a still image from multi-frame recording data.

### `--time <sec>`

Extracts and outputs a specific elapsed timestamp (e.g. `2.5`) or start/end interval (e.g. `1.0-4.0`).

### `--save-frames <dir>`

Saves all still SVG frames produced during video generation as sequentially numbered files in the specified directory.

## Recording and Replay

### `--save-cast <path>`

Saves output during execution as an asciicast v2 file, the standard terminal recording format.

### `--in <path>`

Renders an existing asciicast v2 recording file instead of executing a new command.

### `--replay-save <path>`

Records interactive human keyboard input during command execution into a reusable replay file.

### `--replay <path>`

Loads a saved replay file and automatically replays identical keystrokes to capture the screen.

### `--timeout <sec>`

Specifies the maximum execution wait time in seconds. Automatically terminates the process if it does not finish within this duration.

## Secret Protection

### `--mask <pattern>`

Masks matching strings displayed on screen with asterisks. Can be specified multiple times.

### `--mask-auto [bool]`

Enables or disables automatic secret detection and masking via QuickLeaks (API tokens, private keys, PII, etc.; default: `true`).

## Execution Environment and Advanced Controls

### `--no-resize`

Maintains the initial startup TTY size without resizing by console2svg.

### `--mouse [bool]`

Forwards mouse tracking events from interactive screens to child processes (default: `true`), enabling mouse wheel scrolling in TUI tools.

### `--no-colorenv`

Disables overriding color-related environment variables set on PTY startup (e.g. `COLORTERM=truecolor`).

### `--no-delete-envs`

Preserves CI-related environment variables (such as `CI` or `TF_BUILD`) passed into the child process without automatically stripping them on PTY startup.

### `--coalesce-ms <ms|auto>`

Specifies the coalescing time window (in milliseconds) that batches fragmented output events into single frames.

### `--adjust <spacing|spacingAndGlyphs>`

Selects the SVG `lengthAdjust` attribute method used to align text length with cell widths.

### `--svg-converter <auto|ffmpeg|rsvg-convert|resvg>`

Explicitly specifies the converter engine used to rasterize SVG into PNG or video formats.

### `--verbose [path]`

Displays verbose diagnostic logs to standard error. If a path is specified, logs are written to that file.

## Programmatic and AI Agent Integration

### `--json`

Outputs capture execution results in JSON format to standard output.
Contains plain text screen content, exit code, and file paths to generated images and preview frames, designed for AI agents and automation scripts to parse results easily.

```bash title="Terminal"
console2svg capture --json -- npm test
```

The output JSON includes the following fields:

* `schemaVersion`: Version of the JSON schema
* `status`: Execution outcome (e.g. `completed`)
* `exitCode`: Exit code of the executed child process
* `durationMs`: Total command execution duration in milliseconds
* `screen`: Terminal dimensions, plain text screen content (`text`), and truncation flag (`truncated`)
* `artifacts`: Path and format of the primary generated output file
* `frames`: List of file paths to representative preview frames (still SVGs) generated during video capture (up to 12 frames)

When `--json` is specified, all diagnostic messages are separated to standard error, ensuring standard output contains pure JSON. Note that `--stdout` and `--json` cannot be used simultaneously.
