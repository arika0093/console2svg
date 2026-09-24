---
title: session
description: Subcommands to launch, interact with, and capture background terminal sessions across CLI invocations.
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session read <id> [--wait <duration>]
console2svg session send <id> (--keys <key> | --text <text>)
console2svg session resize <id> --width <columns> --height <rows>
console2svg session capture <id> [-o <path>] [appearance options]
console2svg session stop <id>
console2svg session stop --all [--yes]
```

`session` is a suite of subcommands for managing pseudo-terminal sessions running independently in the background.
Once you start a session, you can read screen text, send keystrokes, or capture current screen state as SVG images from subsequent, distinct CLI calls.
This is especially powerful for stepping through and controlling interactive TUI applications (such as editors, configuration menus, or interactive CLIs) from AI agents or automation scripts.

Note that **all `session` subcommands output structured JSON to standard output** (no flag required to enable JSON output). Diagnostic logs are routed separately to standard error.

## Subcommands and Operational Flow

### 1. Launching a Session: `start`

Spawns a command as a background worker and starts a new terminal session.

```bash title="Terminal"
console2svg session start --width 120 --height 30 -- btop
```

* `--width <columns>`: Terminal width (1–500, default: `100`)
* `--height <rows>`: Terminal height (1–500, default: `24`)
* `--cwd <path>`: Working directory to execute the command in

The response includes a unique `sessionId` (e.g. `s_abc123`), process lifecycle status (`state`), OS process ID, and terminal dimensions.

### 2. Reading Screen State: `read`

Retrieves the current screen content of the session as plain text.

```bash title="Terminal"
console2svg session read s_abc123 --wait 2s
```

* `<id>`: Target session ID
* `--wait <duration>`: Time to wait for screen changes (e.g. `500ms`, `2s`; maximum 60 seconds)

The `screen` object in the response contains terminal width and height, plain text screen content (`text`, up to 200,000 characters), and whether the output was truncated (`truncated`).
When a wait duration is specified, the latest screen is returned as soon as a change occurs or upon timeout (a timeout is a normal response with `timedOut: true`, not an error).

### 3. Sending Keystrokes and Text: `send`

Sends keyboard input or text strings to the running program.

```bash title="Terminal"
# Send string input
console2svg session send s_abc123 --text "git status"
# Send a special key
console2svg session send s_abc123 --keys Enter
# Send a control key (such as Ctrl+C)
console2svg session send s_abc123 --keys Ctrl+C
```

* `<id>`: Target session ID
* `--text <text>`: Sends the specified string as-is without appending newlines.
* `--keys <key>`: Sends a special key. Supported keys: `Enter`, `Tab`, `Escape` (`Esc`), `Backspace`, `Delete`, `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`, `Ctrl+A`–`Ctrl+Z`, or any single printable character.

Calling `session read` immediately after sending input lets you inspect the updated screen after the program responds.

### 4. Resizing Terminal Window: `resize`

Dynamically changes the size of the active virtual terminal window.

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

Sends SIGWINCH (window resize signal) to the child process, triggering supported TUI applications to redraw their screen.

### 5. Capturing Current Screen: `capture`

Saves the session's current screen buffer as a high-quality still SVG image.

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`: Destination SVG file path
* Appearance options: All appearance options from `capture` are supported, including window decorations (`-d`), themes (`-t`), foreground/background colors, fonts, and padding.

### 6. Listing Active Sessions: `list`

Retrieves a list of all currently running sessions.

```bash title="Terminal"
console2svg session list
```

### 7. Terminating a Session: `stop`

Stops the session, terminates the associated process tree, and cleans up resources.

```bash title="Terminal"
# Stop a single session
console2svg session stop s_abc123

# Stop all managed sessions at once
console2svg session stop --all --yes
```

* `<id>`: Session ID to terminate
* `--all`: Stops all sessions managed by console2svg.
* `-y, --yes`: Skips confirmation prompts when stopping all sessions (required in piped execution or automation scripts).

Stopping a session deletes its temporary data, after which `read` and `capture` operations can no longer be performed.
