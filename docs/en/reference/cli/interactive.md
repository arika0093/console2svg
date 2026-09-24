---
title: interactive
description: Command to launch an interactive shell and capture the screen on demand via keybindings.
---

```bash title="Terminal"
console2svg interactive [options]
```

`interactive` is a subcommand that launches an interactive shell in a pseudo-terminal, allowing you to capture screen snapshots or record videos on the spot by pressing shortcut keys during work.
This eliminates the friction of switching to external screenshot tools when writing documentation, letting you capture high-quality SVGs without breaking your terminal workflow.

## Controls and Keybindings

Running the command launches an interactive shell based on your current environment.
Press the following keys at any time during work to record the screen:

* **F9**: Captures the current screen as a still SVG image.
* **F10**: Toggles video recording on and off.
* `exit` command or **Ctrl+D**: Terminates the interactive session.

## Options

`interactive` supports the primary appearance and output options common to `capture`, except for still frame extraction (`--frame`, `--time`) and playback of existing recordings (`--in`, `--replay`).

### Output and Formats

* `-o, --out <path>`: Specifies output file path or destination directory.
* `--format <format>`: Explicitly specifies output format (`svg`, `png`, `gif`, `mp4`, etc.).
* `-v, --video`: Sets the default capture mode to video recording.
* `--stdout`: Writes the generated result to standard output.

### Terminal Size and Layout

* `-w, --width <int|adjust>`: Specifies terminal width in columns (default: `100`).
* `-h, --height <int|adjust>`: Specifies terminal height in rows (default: `24`).
* `--size <WxH>`: Specifies image dimensions when rasterizing.
* `--crop-top`, `--crop-bottom`, `--crop-left`, `--crop-right`: Crops the top, bottom, left, or right edges of the screen in pixels or characters.

### Appearance and Themes

* `-d, --window [style]`: Specifies window decoration style (`macos`, `macos-pc`, `windows`, etc.).
* `-t, --theme <id>`: Specifies appearance theme ID (can be specified multiple times).
* `--forecolor <color>`, `--backcolor <color>`: Overrides foreground or background color.
* `--background <value>`: Specifies window background color or image (specifying twice creates a gradient).
* `--opacity <number>`: Specifies terminal background opacity (`0.0`–`1.0`).
* `--font <family>`, `--fontsize <px>`: Specifies font family and font size.
* `-c, --with-command`: Displays the most recently executed command line at the top of the screen.

### Recording and Masking

* `--mask <pattern>`: Masks the specified string pattern.
* `--mask-auto [bool]`: Enables or disables automatic secret detection and masking via QuickLeaks (default: `true`).
* `--save-cast <path>`: Saves the entire session output as an asciicast v2 file.
* `--fps <number>`: Specifies maximum sampling rate during video recording.
* `--timeout <sec>`: Limits the maximum session duration in seconds.

### Execution Environment Controls

* `--mouse [bool]`: Forwards mouse tracking events to the child process (default: `true`).
* `--no-colorenv`: Disables overriding color-related environment variables.
* `--no-delete-envs`: Preserves CI-related environment variables without automatically stripping them.
* `--svg-converter <converter>`: Specifies the rasterization engine (`auto`, `resvg`, `ffmpeg`, `rsvg-convert`).
* `--verbose [path]`: Specifies the destination for verbose log output.
