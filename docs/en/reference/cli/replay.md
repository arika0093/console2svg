---
title: replay
description: Command to automatically replay saved keyboard input and capture identical operation results.
---

```bash title="Terminal"
console2svg replay <replay.json> [options] -- command [args...]
```

`replay` is a subcommand that loads a keyboard input file (`<replay.json>`) recorded previously (e.g. via `capture --replay-save <path>`), and captures the screen while automatically sending keystrokes to the specified command at identical timings.
It perfectly reproduces interactive human operations (such as text editor interactions or CLI menu selections) to automatically generate up-to-date output screens and animated SVGs.

## Command Behavior

Running `replay` launches the specified command via a PTY (pseudo-terminal) and transmits each keystroke recorded in the replay file (such as Enter, arrow keys, Ctrl combinations, and typed text) according to the scheduled timestamps.
Once all keystrokes have been dispatched and the specified wait duration or command exit is reached, the final screen or video is generated.

## Options

In addition to the keystroke replay file, `replay` supports common output configurations, appearance themes, and video options from `capture`.

### Output and Formats

* `-o, --out <path>`: Specifies output file path (format is automatically inferred from extension).
* `--format <format>`: Explicitly specifies output format (`svg`, `png`, `gif`, `mp4`, etc.).
* `-v, --video`: Outputs as an animated SVG or video format.
* `--stdout`: Writes the generated result to standard output.

### Playback and Timing Control

* `--timeout <sec>`: Specifies the overall execution timeout in seconds.
* `--sleep <sec>`: Specifies additional delay in seconds after all keystrokes complete before finishing the recording.
* `--fadeout <sec>`: Specifies the fadeout duration in seconds at the end of the video.
* `--fps <number>`: Specifies maximum frame rate for video sampling.
* `--timing <deterministic|realtime>`: Selects the timing control strategy for video playback.

### Terminal Size and Screen Cropping

* `-w, --width <int|adjust>`, `-h, --height <int|adjust>`: Specifies terminal width (in columns) and height (in rows).
* `--size <WxH>`: Specifies pixel dimensions of the output image.
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`: Crops the top, bottom, left, or right edges of the screen in pixels or characters.

### Appearance and Themes

* `-d, --window [style]`: Specifies window decoration style (`macos`, `macos-pc`, `windows`, etc.).
* `-t, --theme <id>`: Specifies appearance theme ID.
* `--forecolor <color>`, `--backcolor <color>`: Overrides foreground or background color.
* `--background <value>`: Specifies window background color or image.
* `--opacity <number>`: Specifies terminal background opacity (`0.0`–`1.0`).
* `--font <family>`, `--fontsize <px>`: Specifies font family and font size.
* `-c, --with-command`: Displays the executed command line at the top of the screen.

### Recording and Embedding Diagnostic Info

* `--save-cast <path>`: Saves terminal output during replay as an asciicast v2 file.
* `--embed-cast`: Embeds original asciicast data as metadata inside the generated SVG.
* `--embed-replay`: Embeds the applied keyboard replay data inside the SVG.
* `--embed-logs`, `--embed-debug`: Embeds diagnostic logs or debug information inside the SVG.
* `--mask <pattern>`, `--mask-auto [bool]`: Configures string masking and automatic secret protection.
* `--svg-converter <converter>`: Specifies the rasterization engine (`auto`, `resvg`, `ffmpeg`, `rsvg-convert`).
* `--verbose [path]`: Specifies destination for verbose log output.
