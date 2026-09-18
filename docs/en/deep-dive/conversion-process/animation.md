---
title: Handling animation
description: How time-series terminal states are converted into compact animated SVGs by reusing row definitions and SMIL.
---

The goal of animation output is not to line up frame images, but to make it possible to carry around “what the terminal displayed at each point in time” as a single SVG. Therefore, `AnimatedSvgRenderer` feeds the recording into the terminal emulator in chronological order, generates a `ScreenBuffer` for each point in time, and then lowers it into SVG. When writing video, the same state sequence is used first, so screen interpretation does not diverge between still SVG, animated SVG, and video.

## Why SMIL instead of CSS keyframes?

A straightforward implementation could output each frame as a `<g>` and switch opacity with `@keyframes`. However, with this approach, many frame elements become animation targets during playback, and longer recordings increase browser style calculation and drawing targets. If the whole screen is duplicated for every frame, the SVG also grows rapidly even when most of the terminal has not changed.

Currently, `<animate attributeName="display" calcMode="discrete">` is attached to each display interval. Because `display` switches discretely, intermediate frames do not need to be composited, and only the rows that should be visible are enabled at that time. CSS is used only for blinking text, not as the main mechanism for screen transitions.

## Two-stage reduction to avoid increasing frames

PTY and asciicast event boundaries are not necessarily visual frame boundaries. If events that only change color settings or TUI updates delivered in short chunks are stored as-is, meaningless intermediate states are also output.

`TerminalEmulator.ReplayFrames` compares visible-cell signatures and folds events whose display does not change into the previous frame. If `--fps` is positive, a minimum interval is also applied; when multiple updates arrive within that window, the newest change is held and adopted. This prioritizes a settled screen over continuously displaying a partially drawn TUI.

The first and last frames are always preserved. Frames quantized to the same time are slightly dispersed so that the SMIL time sequence does not go backward or duplicate times.

At the SVG stage, identical rows are detected from each row's visual signature and actual cell comparison. Rows with the same content are drawn once in `<defs>` and referenced with `<use>` at each point in time. If changes are localized, the previous row definition is used as a base and only the changed column range is overwritten as a delta definition. This avoids a “full screen for every frame” data structure and moves size closer to the actual amount of screen change than to recording duration.

## Time, final display, and cursor

Normally, recording times are used as-is. In fixed-FPS mode, times are rounded to frame intervals; in realtime mode, input times are preserved. When a range is selected with `--time`, the last frame before the start is prepended so that the state at the starting point is visible, and the timeline is reset to a 0-second origin.

If `--sleep` is specified, the final frame is held for that duration. If not specified, it is held for at least one frame so that the final state does not disappear instantly. `--fade-out` lowers opacity only after this hold. Because the cursor is defined and switched separately from row text, cursor movement alone does not duplicate text row definitions. Tail events where a full-screen application exits the alternate screen and returns to an empty screen are also detected, leaving the previous non-empty frame as the final display.
