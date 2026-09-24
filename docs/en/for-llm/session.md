---
title: Terminal Automation for LLMs
description: Autonomous TUI manipulation and visual feedback loop for LLMs via a background session daemon.
since: v0.11
---

Just as Playwright enables automated testing for web browsers, an LLM requires a feedback loop to manipulate TUIs (Text User Interfaces): observing screen visual state, dispatching keystrokes, and evaluating resulting changes.
Standard CLI execution terminates as soon as initial command output finishes, making it impossible to interactively navigate stateful applications such as vim, fzf, or interactive setup wizards.

The `console2svg session` suite runs a background daemon process that maintains a pseudo-terminal (PTY) session, providing a JSON-based programmatic interface for LLMs to execute step-by-step terminal operations.
LLMs utilize terminal text buffers and rendered SVG snapshots as their "eyes," enabling autonomous trial-and-error loops to adjust TUI layouts or verify visual behavior.

## Agent Prerequisites

To instruct an LLM agent on terminal operation procedures, load the pre-configured [SKILL.md](./use-skill.md) into the agent's prompt or system instructions.
The agent follows these instructions to execute the JSON-based subcommands detailed below.

All `console2svg session` subcommands return responses to stdout in JSON format.
This allows LLMs to reliably inspect exit statuses and screen states as structured objects without relying on brittle regex parsing.

## Basic Interactive Workflow

An interactive session workflow consists of five stages: start, resize, observe, send input, and stop.

### 1. Starting a Session

Launch a background session by specifying the target command:

```bash title="Terminal"
console2svg session start -- bash
```

Upon successful startup, a JSON object containing a unique `sessionId` is returned.
Use this ID in all subsequent subcommands:

```json title="Sample Output"
{
  "sessionId": "s_a1b2c3d4e5f6",
  "state": "running",
  "command": "bash",
  "width": 80,
  "height": 24,
  "createdAt": "2025-01-15T10:00:00Z"
}
```

### 2. Resizing the Screen Dimensions

Many TUI applications adjust their layout depending on terminal column and row dimensions.
Use `session resize` to set the desired dimensions:

```bash title="Terminal"
console2svg session resize s_a1b2c3d4e5f6 --width 120 --height 30
```

### 3. Observing the Screen State

To evaluate how the application responded to previous inputs, retrieve the terminal text buffer or capture an SVG image:

```bash title="Terminal"
# Wait up to 1 second for output stream updates and read text buffer
console2svg session read s_a1b2c3d4e5f6 --wait 1s

# Capture the current visual screen as an SVG image
console2svg session capture s_a1b2c3d4e5f6 -o /tmp/current-screen.svg
```

`session read` returns the screen text and cursor coordinates, while `session capture` exports actual colors, styling, and geometry.
Multimodal LLMs can directly inspect the resulting SVG image to detect layout misalignment or color contrast anomalies.

### 4. Sending Keystrokes and Input

Once the current screen state is verified, send the next sequence of keys to the terminal:

```bash title="Terminal"
# Send arbitrary text string
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# Send special keys like Enter or arrow keys
console2svg session send s_a1b2c3d4e5f6 --keys Enter
```

Text and special keys can be dispatched sequentially as needed.
After sending input, call `session read` again to verify that the terminal reached the expected state.

### 5. Terminating the Session

Once the interactive workflow is complete, explicitly terminate the background process:

```bash title="Terminal"
# Terminate a specific session
console2svg session stop s_a1b2c3d4e5f6

# Terminate all running sessions at once
console2svg session stop --all --yes
```

Terminating a session closes the PTY, stops associated child processes, and cleans up the Unix domain socket files.
