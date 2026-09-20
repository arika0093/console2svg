---
title: Record/play replays
description: Replay functionality that records input keystrokes and automatically reproduces the same operations for capture.
since: v0.9
---

Save keyboard input and timing to a JSON file, then reproduce the exact same operations later for recapture. This is suitable for regular documentation image updates and CI automation.

## Recording a replay

Run with a destination file path specified for the `--replay-save` option.

```bash title="Terminal" "--replay-save demo.json"
# Record an interactive session
console2svg interactive --replay-save demo.json -- bash
```

All keystrokes and time intervals during execution are saved to `demo.json`.

## Playing a replay

Use the `replay` subcommand to run capture with a saved replay file.

```bash title="Terminal" "replay demo.json"
console2svg replay demo.json -- bash
```

![console2svg replay demo.json -- bash](/docs/assets/cmd-bash-vim.svg)

> [!TIP]
> You can freely specify themes, video output options, and other options during playback as well.
