---
title: Interactive capture
description: Capture an interactive shell and save terminal states or recordings.
---

The `interactive` workflow starts your normal shell in a PTY and forwards it to your terminal. Keep working as usual, then save the screen you are currently looking at or record a portion of the session. On Unix it uses `$SHELL`; on Windows it uses the system command shell.

```bash
console2svg interactive -d macos -o captures/output.svg
```

During an interactive session:

- Press `F10` to save the current screen as a static SVG.
- Press `F9` to start or stop an animated recording.
- Press `F12` to pause or resume an active recording.
- Press `Ctrl+D` or exit the shell to finish the session.

The capture controls are handled by console2svg and are not sent to the shell. While a recording is paused, output and elapsed time are excluded. Use [recording and replay](/advanced-usage/recording-replay-and-cast-files/) when you need to reproduce the same input later.

![An interactive terminal capture](/assets/cmd-interactive.svg)
