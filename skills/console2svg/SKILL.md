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

Use a managed session when a TUI must stay open across commands and requires input based on its current screen:

```bash
console2svg session start -- btop
console2svg session list
console2svg session read <sessionId>
console2svg session wait <sessionId> --text "Ready"
console2svg session wait <sessionId> --text "Working" --until absent --stable-for 2s
console2svg session send <sessionId> --keys Enter
console2svg session send <sessionId> --text "search query"
console2svg session send <sessionId> --text "i" --keys Enter --text "hello" --keys Esc
console2svg session resize <sessionId> --width 120 --height 40
console2svg session capture <sessionId> -o /tmp/tui-screen.svg
console2svg session inspect <sessionId>
console2svg session capture <sessionId> -o /tmp/tui-screen-converted.png
console2svg session stop <sessionId>
```

`--text` and `--keys` may be repeated in one `session send`; inputs are sent in argument order.

## Documentation

- [Capture command reference](https://console2svg.eclairs.cc/en/reference/cli/capture/)
- [Video capture guide](https://console2svg.eclairs.cc/en/basic-usage/capturing-videos/overview/)
- [Managed session reference](https://console2svg.eclairs.cc/en/reference/cli/session/)
- [Interactive capture reference](https://console2svg.eclairs.cc/en/reference/cli/interactive/)
- [tmux command reference](https://console2svg.eclairs.cc/en/reference/cli/tmux/)
