---
title: status
description: Command to display and diagnose the execution environment, renderers, and external tool detection status.
---

```bash title="Terminal"
console2svg status [--format table|markdown|json]
console2svg status --json
```

`status` is a subcommand that comprehensively diagnoses and displays the current system environment, SVG renderers (resvg, ffmpeg, rsvg-convert), installed themes, and feature availability.
Use it to verify whether environment-dependent external tools are functioning properly, and to gather environmental information for troubleshooting or issue reporting.

## Diagnostic Categories

Running the command produces detailed detection results organized into the following categories:

* **Application**: console2svg version number, executable absolute path, Native AOT status, and distribution channel
* **Platform**: OS name and kernel version, CPU architecture, and .NET runtime version
* **Renderers**: Detection status, versions, and paths for bundled resvg, rsvg-convert, and ffmpeg, as well as codecs available for MP4 encoding (e.g. libx264)
* **Features**: Availability determination for features such as video capture and tmux pane integration
* **Themes**: Number of recognized built-in and custom themes
* **Colors / Formats**: Terminal ANSI color support status and available output format list

## Options

### `--format <table|markdown|json>`

Specifies the output format:

* `table` (default): Outputs a neatly formatted color table suitable for terminal viewing.
* `markdown`: Outputs in Markdown format suitable for pasting directly into GitHub Issues or Pull Requests.
* `json`: Outputs structured JSON suitable for scripted processing and CI diagnostic steps.

### `--json`

Outputs diagnostic results in JSON format to standard output, identical to `--format json`. Available as a shorthand flag.
