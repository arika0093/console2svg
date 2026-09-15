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

## Full option table

The table below is a reading guide. Always confirm flags with the installed binary, as commands evolve between releases:

```bash
console2svg --help
console2svg capture --help
console2svg replay --help
console2svg convert --help
console2svg theme --help
console2svg status --help
console2svg completions generate --help
```

| Command | Key options |
| --- | --- |
| `capture` | `-o/--out`, `-w/-h/--size`, `-v`, `--fps`, `--sleep`, `--timeout`, `--crop-top/right/bottom/left`, `--theme`, `-d`, `-t`, `--background`, `--opacity`, `--margin`, `--padding`, `--pc-padding`, `--prompt`, `--header`, `--forecolor`, `--backcolor`, `--mask`, `--verbose`, `--stdout` |
| `replay` | Replay-file path, `-w/-h`, `-v`, appearance and timing options shared with `capture` |
| `convert` | Input `.cast`/`.svg` path, `-o/--out` target format (`png`, `gif`, `mp4`, `webm`) |
| `interactive` | `-d/--theme`, `-o/--out`, on-demand keys (`F9` start/stop) |
| `live-server`, `tmux live-server` | Port/preview options; streams the terminal as SVG |
| `tmux capture` | Target pane, `-w/-h`, appearance options shared with `capture` |
| `theme list/install/remove/update` | Theme id, package directory/archive/URL source |
| `status [--json\|--markdown]` | Runtime, renderer (`resvg`, `rsvg-convert`, `ffmpeg`), theme counts, output formats |
| `completions generate <bash\|zsh\|fish\|powershell>` | Shell name; writes the completion script to stdout |
