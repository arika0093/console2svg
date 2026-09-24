---
title: Let an LLM operate the terminal
description: Use console2svg with an LLM to capture and inspect command results.
since: v0.11
---

Structured terminal operations are available for use by LLMs.

## Motivation

This provides a terminal-oriented tool like [`playwright-cli`](https://github.com/microsoft/playwright-cli), giving AI a way to "see" terminal output.

> [!TIP]
> This lets an LLM handle terminal operations autonomously and verify the results in a feedback loop for visual requests, such as improving an interface's appearance.

## Usage

Load [SKILL.md](./use-skill.md) into your LLM.

## Overview

Use [console2svg session](../reference/cli/session.md) to let an LLM operate a terminal.

> [!NOTE]
> Each command returns output in JSON format to make it easier for LLMs to parse.

### Start a session

Starting a session returns a session ID.

```bash title="Terminal"
# Start a session
console2svg session start -- bash
# > {"sessionId":"s_randomhash1234","state":"running", ...}
```

### Resize the terminal

Some TUIs depend on the terminal size at startup. Resize it as needed.

```bash title="Terminal"
console2svg session resize s_randomhash1234 --width 160 --height 40
```

### Inspect the screen

Read the screen text or capture an image to inspect the current state.

```bash title="Terminal"
# Read the terminal text
console2svg session read s_randomhash1234 --wait 1s
# Capture an image
console2svg session capture s_randomhash1234 -o /tmp/current-screen.svg
```

### Send input

Send input to interact with the TUI.

```bash title="Terminal"
# Send text
console2svg session send s_randomhash1234 --text "search query"
# Send a special key
console2svg session send s_randomhash1234 --keys Enter
```

### Stop a session

```bash title="Terminal"
# Stop a session
console2svg session stop s_randomhash1234
# Stop all sessions
console2svg session stop --all --yes
```
