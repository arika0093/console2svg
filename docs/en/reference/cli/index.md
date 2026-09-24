---
title: CLI Reference
description: Comprehensive reference for all console2svg subcommands and options.
---

`console2svg` provides subcommands for recording terminal sessions, converting them to vector images or videos, managing themes, and diagnosing operating environments.
When running commands to capture, specify the target command line after the `--` delimiter to avoid option parsing ambiguities.

```bash title="Terminal"
console2svg capture [options] -- command [args...]
```

## Subcommands

The following subcommands are available depending on your use case.

### Screen Recording and Rendering

| Command | Purpose |
| --- | --- |
| [`capture`](./capture.md) | Runs a specified command in a pseudo-terminal and records the final screen or animation as SVG/video. |
| [`interactive`](./interactive.md) | Starts an interactive shell and records the screen interactively via keybindings at any time. |
| [`replay`](./replay.md) | Replays previously recorded keyboard input and captures identical operation results. |
| [`cast`](./cast.md) | Loads an existing asciicast v2 recording file and renders it as an SVG image or animation. |

### Session Management and External Integration

| Command | Purpose |
| --- | --- |
| [`session`](./session.md) | Starts and controls a persistent background pseudo-terminal session, reading current screen state. |
| [`live-server`](./live-server.md) | Converts a running terminal screen to SVG in real time and streams it over HTTP for browsers. |
| [`tmux`](./tmux.md) | Targets an active tmux pane to capture its screen directly or stream it live. |

### Document Automation and Agent Assistance

| Command | Purpose |
| --- | --- |
| [`batch`](./batch.md) | Scans embedded tags in Markdown/MDX to automatically generate and synchronize documentation images. |
| [`llm`](./llm.md) | Outputs built-in Skill definitions to allow AI agents (such as GitHub Copilot or Claude) to operate console2svg. |

### Environment Management and Utilities

| Command | Purpose |
| --- | --- |
| [`theme`](./theme.md) | Lists, installs, updates, and removes terminal appearance themes and color palettes. |
| [`status`](./status.md) | Diagnoses the execution environment, renderers (resvg, ffmpeg, etc.), and available features. |
| [`update`](./update.md) | Checks for the latest console2svg release and performs a self-update. |
| [`completions`](./completions.md) | Generates shell completion scripts for bash, zsh, fish, PowerShell, etc. |
