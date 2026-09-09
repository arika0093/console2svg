---
title: CLI reference
description: Overview of console2svg commands and option groups.
---

Use `console2svg --help` for the installed version's complete command reference. Commands can evolve between releases, so this page groups the options you are most likely to need rather than duplicating every help entry.

## Commands

| Command | Purpose |
| --- | --- |
| `capture` | Run a command or read piped output and render it. |
| `interactive` | Run an interactive shell and capture it on demand. |
| `replay` | Render a saved replay file. |
| `convert` | Render an asciicast file or convert an existing SVG. |
| `live-server` | Serve a live terminal in a browser. |
| `tmux capture` | Capture a tmux pane. |
| `tmux live-server` | Stream a tmux pane in a browser. |
| `theme` | Manage terminal themes. |
| `status` | Report runtime and converter information. |

## Common option groups

- Output: `--out` (`-o`), `-v`, `--stdout`, and frame output.
- Dimensions: `-w`, `-h`, `--size`, and size adjustment.
- Timing: `--fps`, `--sleep`, `--timeout`, and video timing.
- Cropping: `--crop-top`, `--crop-right`, `--crop-bottom`, and `--crop-left`.
- Appearance: themes, colors, fonts, window chrome, backgrounds, padding, and margins.
- Safety: `--mask` and embedded metadata options.
- Diagnostics: `status`, `--verbose`, and verbose log output.

> [!TIP]
> Put `--` before a captured command, especially when it has options that look like console2svg options. For example: `console2svg capture -w 100 -- git log --oneline`.

<!-- TODO: Generate and embed a searchable CLI option table from `console2svg --help`. -->
