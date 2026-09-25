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
On failure, branch on `error.code`; `error.message` and standard-error diagnostics are for people and may change.

## Basic Interactive Workflow

An interactive session workflow consists of seven stages: start, resize, inspect/read, wait and send, capture, export, and stop.

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

### 3. Screen Inspection and Observation (read / inspect)

To evaluate screen state, retrieve the text buffer or inspect the rendered terminal:

```bash title="Terminal"
# Read current screen text immediately
console2svg session read s_a1b2c3d4e5f6

# Read structured text with per-cell style, hyperlink, and wide-character metadata
console2svg session read s_a1b2c3d4e5f6 --structured

# Render an ephemeral SVG for visual inspection (path is auto-allocated)
console2svg session inspect s_a1b2c3d4e5f6
```

#### Role of inspect and read (Observations for Diagnostics Only)

`session read` and `session inspect` are **Observations** designed for exploration and diagnostics.
They are inspection-only operations and do not represent durable action steps.

* `session read` immediately returns viewport plain text, cursor coordinates, and alternate-screen state.
* `session inspect` renders the terminal to an ephemeral temporary SVG under a randomized directory, returning the path without requiring the caller to specify an output filename.
* Both `read` and `inspect` are automatically omitted from Scenario export so that temporary diagnostic checks do not pollute the exported scenario.

#### Agent Design Principle: Materializing Observations as Conditions

When an agent observes terminal state via `read` or `inspect` and decides on a subsequent action, it must materialize that dependency as a **Condition** (such as `session wait`) before executing the action (`session send`).

For example, when `read` reveals `Overwrite? [y/N]`, do not immediately issue `send --text "y"`.
Instead, establish the condition first:

```bash title="Terminal"
# 1. Inspect screen state (Observation)
console2svg session read s_a1b2c3d4e5f6

# 2. Materialize the dependency as a Condition
console2svg session wait s_a1b2c3d4e5f6 --text "Overwrite? [y/N]"

# 3. Perform the action (Action)
console2svg session send s_a1b2c3d4e5f6 --text "y" --keys Enter
```

Anchoring actions to explicit conditions ensures that exported scenarios can reproduce the interaction deterministically.

### 4. Waiting for Conditions and Sending Input (wait / send)

#### Waiting for Conditions (wait)

Wait for text to appear, or for previously seen text to disappear:

```bash title="Terminal"
# Wait for text to appear
console2svg session wait s_a1b2c3d4e5f6 --text "Ready"

# Wait for text to disappear and require 2 seconds of stability
console2svg session wait s_a1b2c3d4e5f6 --text "Working" --until absent --stable-for 2s
```

#### Sending Input (send)

Once the condition is satisfied, transmit inputs to the terminal:

```bash title="Terminal"
# Send plain text
console2svg session send s_a1b2c3d4e5f6 --text "git status"

# Send named special keys
console2svg session send s_a1b2c3d4e5f6 --keys Enter

# Send ordered sequences of text, paste, and keys
console2svg session send s_a1b2c3d4e5f6 --text "i" --keys Enter --paste "hello" --keys Esc
```

`--text`, `--paste`, `--keys`, and `--raw-hex` may be repeated; inputs are sent in argument order as a single atomic operation.

### 5. Capturing Durable Artifacts (capture)

Unlike ephemeral `inspect`, use `session capture` when saving durable image artifacts:

```bash title="Terminal"
console2svg session capture s_a1b2c3d4e5f6 -o docs/assets/status.svg -d macos -t dracula
```

Because `capture` represents an intended output artifact, it is recorded as an Action and included in Scenario export.

### 6. Exporting to a Scenario Document (export)

A successful interactive path can be exported into a runnable **Scenario document** (YAML):

```bash title="Terminal"
console2svg session export s_a1b2c3d4e5f6 -o tests/scenarios/setup.yaml
```

Export extracts the reproducible sequence of `send`, `wait`, `resize`, and `capture` operations while dropping exploratory `read` and `inspect` observations.
The resulting scenario file can be re-run directly with `console2svg scenario run`:

```bash title="Terminal"
console2svg scenario run tests/scenarios/setup.yaml
```

This enables agent-explored workflows to be converted into automated CI regression tests without human or LLM intervention on subsequent runs.

### 7. Terminating the Session (stop)

Once the interactive workflow is complete, explicitly terminate the background session:

```bash title="Terminal"
# Terminate a specific session
console2svg session stop s_a1b2c3d4e5f6

# Terminate all running sessions at once
console2svg session stop --all --yes
```

Terminating a session closes the PTY, stops associated child processes, and cleans up temporary IPC sockets.
Use `session list` (or `session list --all`) to inspect active or retained sessions.
