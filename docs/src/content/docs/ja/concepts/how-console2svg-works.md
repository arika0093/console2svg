---
title: How console2svg works
description: Understand the capture, terminal emulation, and rendering pipeline.
---

console2svg records terminal output, replays it through a terminal emulator, and renders the resulting screen as SVG. Optional converters then produce PNG, GIF, MP4, or WebM from the same frames.

```text
pipe input ─┐
PTY capture ─┼─> terminal emulation ─> SVG rendering ─> output.svg
replay file ─┤         (ANSI/SGR,        (chrome,           └─> png/gif/mp4/webm
tmux pane ──┘          screen buffer)     backgrounds,          (via resvg/ffmpeg)
                                          animation)
```

## Inputs

- **Pipe:** `some-command | console2svg capture` renders streamed stdout without a PTY.
- **PTY:** `console2svg capture -- command` runs the command in a pseudo-terminal so full-screen apps (`btop`, `vim`, `cmatrix`) behave as on a real terminal.
- **Replay:** `console2svg replay ./replay.json` re-renders recorded keyboard input and timing, which keeps CI output deterministic.
- **tmux:** `console2svg tmux capture` captures a tmux pane, useful for long-running or detached sessions.

## Terminal emulation and rendering

The recorder output is parsed as ANSI/VT sequences (SGR colors, cursor moves, scrolling) into a screen buffer. The SVG renderer then applies the selected `--theme`/`-d` chrome, fonts, padding, backgrounds, and video timing (`-v`, `--fps`, `--sleep`, `--timeout`). See [SVG format and style](/concepts/svg-format-and-style/) for the output structure and [file formats](/reference/file-formats-and-embedded-metadata/) for replay/cast/log metadata.

Implementation entry points: `src/ConsoleToSvg.Record` (capture/replay), `src/ConsoleToSvg.Core/Terminal` (ANSI parser, screen buffer, themes), `src/ConsoleToSvg.Core/Svg` (SVG builder/renderer), `src/ConsoleToSvg.Converter` (format conversion).
