---
name: console2svg
description: Use console2svg when an agent needs to inspect a terminal screen, control a TUI in a managed PTY, capture a tmux pane, or create visual terminal evidence.
---

# console2svg

For ordinary non-interactive commands, run the command directly and use its text output.
Use console2svg when terminal rendering matters, when a TUI requires adaptive input, or when an image/video artifact is useful.

## One-shot command capture

Capture a command and get both its final screen text and a visual artifact reference:

```bash
console2svg capture --json -o /tmp/test-run.svg -- npm test
```

The JSON result is versioned and contains
`schemaVersion`, `status`, `exitCode`, `durationMs`,
`screen` (`width`, `height`, `text`, `truncated`), and `artifact` (`path`, `format`).
JSON goes to stdout; diagnostics go to stderr. The command's non-zero exit code is reported in `exitCode`.

For an existing tmux pane:

```bash
console2svg tmux capture --target %1 --json -o /tmp/pane.svg
```

For video or animated SVG output, use `--video`.
JSON automatically includes up to 12 sampled intermediate SVG files in `frames` (`timeMs`, `path`),
so there is no need to know `--save-frames`.
Open the referenced image/frame paths with an available visual inspection tool;
do not inline SVG or image data into a text response.

## Managed TUI sessions

Use a managed session when a TUI must stay open across commands and requires input based on its current screen.
All `session` subcommands output structured JSON to stdout and diagnostics to stderr.
On failure, branch on `error.code`.

### Subcommands

#### 1. `start` — Launch a background session
Spawns a command in a managed pseudo-terminal (PTY) and returns a unique `sessionId`.
```bash
console2svg session start --width 120 --height 30 -- btop
```
* Options: `--width <cols>`, `--height <rows>`, `--cwd <path>`

#### 2. `read` — Read current screen text
Returns the viewport text, cursor position, and alternate-screen state. Use `--structured` for styled cell metadata.
An Observation for exploratory diagnostics; excluded from Scenario export.
```bash
console2svg session read <sessionId> [--structured]
```

#### 3. `inspect` — Ephemeral visual inspection
Renders the current screen to a temporary SVG under a randomized private directory and returns `{path}`.
An Observation for visual checks without choosing an output path; excluded from Scenario export.
```bash
console2svg session inspect <sessionId> [-t theme] [-d window]
```

#### 4. `wait` — Wait for terminal conditions
Blocks until literal text appears or disappears. Returns matched screen and version.
A Condition that establishes state dependency; included in Scenario export.
```bash
console2svg session wait <sessionId> --text "Ready"
console2svg session wait <sessionId> --text "Working" --until absent --stable-for 2s --timeout 10s
```
* Options: `--text <str>` (required), `--until present|absent`, `--stable-for <duration>`, `--timeout <duration>`

#### 5. `send` — Transmit keystrokes and text
Sends text, bracketed paste, semantic keys, or raw bytes atomically in argument order.
An Action; included in Scenario export.
```bash
console2svg session send <sessionId> --text "git status" --keys Enter
console2svg session send <sessionId> --text "i" --keys Enter --paste "hello" --keys Esc
```
* Options: `--text <str>`, `--keys <key>` (`Enter`, `Tab`, `Esc`, `Ctrl+C`, arrows, etc.), `--paste <str>`, `--raw-hex <hex>`

#### 6. `resize` — Resize terminal dimensions
Sends SIGWINCH to resize terminal columns and rows.
An Action; included in Scenario export.
```bash
console2svg session resize <sessionId> --width 140 --height 45
```

#### 7. `capture` — Save durable screen artifact
Renders the current screen to a durable SVG file with full appearance and theme options.
An Action; included in Scenario export.
```bash
console2svg session capture <sessionId> -o output.svg -t dracula -d macos
```

#### 8. `export` — Export session as Scenario
Extracts the session's actions (`send`, `resize`, `capture`) and conditions (`wait`) into a runnable Scenario YAML file, omitting exploratory `read`/`inspect`.
The exported scenario can be re-run deterministically with `console2svg scenario run <path>`.
```bash
console2svg session export <sessionId> -o scenario.yaml
```

#### 9. `list` — List managed sessions
Lists active sessions. Use `--all` to include retained exited sessions.
```bash
console2svg session list [--all]
```

#### 10. `stop` — Terminate sessions
Closes the PTY, terminates child processes, and cleans up temporary IPC sockets.
```bash
console2svg session stop <sessionId>
console2svg session stop --all --yes
```

### Agent interaction rules

- **Observations vs conditions**: `read` and `inspect` are Observations for exploration. If the next action depends on what was observed, materialize that dependency with `wait` before sending input (`send`).
- **Inspect vs capture**: Use `inspect` for ephemeral visual checks (no path required). Use `capture -o` when a durable artifact is required.
- **Exporting reproducible scenarios**: Run `session export <sessionId> -o <path>` to save the successful interaction path. Re-execute it in CI or tests via `console2svg scenario run <path>`.

## Documentation

- [Capture command reference](https://console2svg.eclairs.cc/en/reference/cli/capture/)
- [Video capture guide](https://console2svg.eclairs.cc/en/basic-usage/capturing-videos/overview/)
- [Managed session reference](https://console2svg.eclairs.cc/en/reference/cli/session/)
- [Scenario command reference](https://console2svg.eclairs.cc/en/reference/cli/scenario/)
- [Interactive capture reference](https://console2svg.eclairs.cc/en/reference/cli/interactive/)
- [tmux command reference](https://console2svg.eclairs.cc/en/reference/cli/tmux/)
