---
title: Recording, replay, and cast files
description: Record terminal input and output for repeatable captures.
---

Recording separates terminal input and output from rendering. Capture a session once, then render it again with different dimensions, themes, or window chrome without manually repeating the same keystrokes. This is particularly useful for demos generated in CI.

## Save keyboard input for replay

```bash
console2svg capture --replay-save replay.json -- bash
```

Render the saved input again later:

```bash
console2svg replay replay.json -d macos -v -- bash
```

Replay files are JSON and can be reviewed or edited when necessary. The first event uses an absolute time and later events use elapsed ticks, which keeps a recording compact while preserving its pacing.

## Cast files

Asciicast-compatible cast files represent timed terminal events. Save one with `--save-cast capture.cast`, or use `convert` when the input is already an asciicast file. They are useful when a recording needs to be exchanged with other terminal-recording tools.

The file structure and metadata options are described in the [reference](/reference/file-formats-and-embedded-metadata/).

![A replayed interactive session](/assets/cmd-bash-vim.svg)

> [!CAUTION]
> Replay and cast files can contain typed commands and terminal output. Review them before publishing and use [masking](/basic-usage/masking-sensitive-output/) for generated images.
