---
title: session
description: Subcommands to launch, interact with, and capture background terminal sessions across CLI invocations.
---

```bash title="Terminal"
console2svg session start [options] -- command [args...]
console2svg session list
console2svg session list --all
console2svg session read <id> [--structured]
console2svg session wait <id> --text <literal> [--until present|absent] [--stable-for <duration>] [--timeout <duration>]
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

Returns the current screen content of the session as plain text.

```bash title="Terminal"
console2svg session read s_abc123
```

* `<id>`: Target session ID

The `screen` object contains terminal width and height, plain text content (`text`, up to 200,000 characters), truncation status, zero-based cursor row and column, cursor visibility, whether the alternate screen is active, the number of scrollback rows, and `scope: "viewport"`. Use `--structured` to include the versioned row-major cell snapshot with per-cell styles, hyperlinks, and wide-character metadata.

```bash title="Terminal"
console2svg session read s_abc123 --structured
```

### 3. Waiting for screen text: `wait`

Waits for literal text to appear or disappear from the screen. Matching is case-sensitive and uses substring matching.

```bash title="Terminal"
console2svg session wait s_abc123 --text "Hi! How can I help?"
console2svg session wait s_abc123 --text "Working" --until absent --stable-for 2s --timeout 3m
```

* `--text <literal>`: Required literal text to match
* `--until <present|absent>`: Match when the text is present (default) or absent
* `--stable-for <duration>`: Require the condition to remain true for this duration (default: no delay)
* `--timeout <duration>`: Optional timeout; there is no maximum, and omitting it waits until the condition is met, the session ends, or the command is cancelled

For `--until absent`, the text must first have appeared in a screen before its disappearance can match. Durations accept `ms`, `s`, `m`, or `h` units (for example, `500ms`, `2s`, `3m`, `1h`); a number without a unit means seconds.
The JSON response includes `result` (`matched`, `timeout`, or `session-ended`), `matched`, `timedOut`, the latest screen, and its version. The command exits with status 0 only for `matched`; timeout and session end return status 1. Ctrl+C cancels an unbounded wait.

### 4. Sending Keystrokes and Text: `send`

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

Repeated `--text` and `--keys` options are sent in their command-line order as one host request, so another client cannot insert input between the steps.

`--raw-hex <bytes>` sends an explicit non-empty byte sequence written as pairs of hexadecimal digits (for example, `1B5B41` sends `ESC [ A`). Semantic names include `Insert`, `F1`–`F12`, `Shift+Tab`, `Shift+Up`, `Ctrl+Alt+Left`, and `Meta+Home`. `Alt` and `Meta` prefix a printable character with Escape; modified navigation and function keys use xterm modifier sequences. Raw bytes use `--raw-hex` and remain distinct from semantic keys.

Calling `session read` immediately after sending input lets you inspect the updated screen after the program responds.

### 5. Resizing Terminal Window: `resize`

Dynamically changes the size of the active virtual terminal window.

```bash title="Terminal"
console2svg session resize s_abc123 --width 140 --height 45
```

Sends SIGWINCH (window resize signal) to the child process, triggering supported TUI applications to redraw their screen.

### 6. Capturing Current Screen: `capture`

Saves the session's current screen buffer as a high-quality still SVG image.

```bash title="Terminal"
console2svg session capture s_abc123 -o current-screen.svg -d macos -t dracula
```

* `-o <path>`: Destination SVG file path
* Appearance options: All appearance options from `capture` are supported, including window decorations (`-d`), themes (`-t`), foreground/background colors, fonts, and padding.

### 7. Listing Sessions: `list`

By default, lists sessions that are starting or running. Pass `--all` to include retained exited or unavailable sessions; retained entries include `expiresAt` when known.

```bash title="Terminal"
console2svg session list --all
```

### 8. Terminating a Session: `stop`

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
