---
name: console2svg
description: Use console2svg when a task needs a terminal screenshot, an animated terminal capture, or a visual capture of an existing tmux pane.
---

# console2svg

Use `console2svg` to create or share visual terminal captures. For ordinary
commands where their text output is enough, run the command directly instead.
console2svg currently creates artifacts; it does not provide an agent-managed
interactive session API.

## Capture a command

Run a command in a PTY and save its final screen as SVG:

```bash
console2svg capture -o terminal-capture.svg -- <command> [args...]
```

For a command that updates its screen over time, use video mode and set a
timeout for commands that do not exit on their own:

```bash
console2svg capture --video --timeout 5 -o terminal-demo.svg -- <command> [args...]
```

The `.svg` output in video mode is animated. Other supported output extensions
can produce formats such as GIF or MP4; those conversions may need external
tools. Use `console2svg capture --help` for the installed version's options.

## Capture a tmux pane

Capture an existing pane by its tmux target:

```bash
console2svg tmux capture --target %1 -o tmux-pane.svg
```

Use `--history` when scrollback is needed. This creates a capture artifact; it
does not send input to the pane or manage the lifetime of its process.

## Interactive capture

`console2svg interactive` is a human-operated capture workflow. The user
controls the child program and uses the documented capture hotkeys to start
and stop recording. Do not present it as an API for an agent to adaptively
send input and read screen state.

`console2svg live-server` is for viewing a running terminal in a browser. Use
it when a live visual view is useful, not as a substitute for reading normal
command output.

## Use and share artifacts

- If a visual inspection tool is available, open the generated file by its
  path. Do not paste the full SVG or encoded image data into a text response.
- For a video, inspect a few representative points when needed; do not assume
  that the final screen describes the whole process.
- Choose an output path that will not overwrite an existing user file.
- Captures can contain secrets or other sensitive terminal content. Masking
  is not a guarantee that every sensitive value will be removed; inspect
  artifacts before sharing them.

## Documentation

- [Capture command reference](https://console2svg.eclairs.cc/en/reference/cli/capture/)
- [tmux command reference](https://console2svg.eclairs.cc/en/reference/cli/tmux/)
- [Interactive capture reference](https://console2svg.eclairs.cc/en/reference/cli/interactive/)
- [Video capture guide](https://console2svg.eclairs.cc/en/basic-usage/capturing-videos/overview/)
