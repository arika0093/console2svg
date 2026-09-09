---
title: tmux
description: Capture or stream a tmux pane as SVG.
---

console2svg can capture a tmux pane on Linux and macOS. On Windows, run the workflow inside WSL. This is useful when a command sequence already lives in tmux: keep working in one pane and save the current state or full history from another.

## Capture a pane

```bash
console2svg tmux capture \
  --target :0 \
  --history \
  -h 12 \
  -o capture.svg
```

If `--target` is omitted, console2svg lets you select a pane interactively.

## Stream a pane

Use the live-server action to update a browser view as the pane changes:

```bash
console2svg tmux live-server --target :0 --fps 2
```

The same `--target`, appearance, output, and timing options can be used across tmux workflows. Omit `--target` to choose a pane interactively. Add `--history=<lines>` to include a fixed number of previous lines, or use `--history` by itself to include all history.

![A tmux pane capture](/assets/cmd-tmux-cap.svg)

![A tmux recording rendered as an animated SVG](/assets/cmd-tmux-replay.svg)
