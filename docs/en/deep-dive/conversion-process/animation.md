---
title: Building animated SVG
description: How retained terminal states become shared row definitions and discrete SMIL visibility intervals.
---

Animated SVG starts from terminal states, not from screenshots.
`AnimatedSvgRenderer` replays the recording through the same terminal emulator used for still output, retains the states that matter at the configured timing, and then converts those states into reusable row definitions.

## Retaining terminal states

A PTY or asciicast event is not automatically an animation frame.
One application redraw can arrive as several writes, and some events change parser state without changing visible cells.

The production replay path compares `ScreenBuffer.GetContentSignature()` after each event.
This signature excludes the cursor, so cursor-only changes do not force a new content snapshot.
When `--fps` is positive, a minimum frame interval is applied.
If several visible changes occur inside that interval, the latest changed state is kept as a pending frame.

Snapshots use copy-on-write rows.
Creating a visible snapshot shares unchanged row arrays with the live screen, and a row is copied only before a later mutation.
Updating a pending frame copies row references and signature metadata instead of deep-copying the complete cell grid.

The first and final states are retained.
If time normalization collapses several frames onto the same timestamp, their times are spread slightly so that the SMIL key-time sequence remains ordered.

## Cataloging rows instead of frames

Once the retained states are known, console2svg does not serialize every complete screen.

`PrepareAnimatedRows` visits each visible row and reads its row visual signature.
The signature selects candidate definitions, but equality is confirmed against the actual row cells before reuse.
This keeps the signature as an acceleration structure rather than a correctness assumption.

When a new unique row is discovered, its text styles are collected at the same time.
Repeated rows therefore do not need a separate style scan in a later pass.

Each frame receives an array of row-definition indices.
The SVG can then define each unique row once under `<defs>` and place it with `<use>` wherever that row state is needed.

## Encoding small row changes as deltas

Typing and status displays often change only a few columns of a row.
For those cases, a new row definition can reference the previous definition and draw only the changed column range.

Delta rows are deliberately restricted.
The changed range must be no more than 16 columns and no more than one quarter of the visible row, and a delta chain may be at most four levels deep.
The range is expanded when it touches a wide-character continuation or the leading half of a wide character.

Manual mask patterns disable this optimization.
A secret pattern may span an unchanged prefix and a changed suffix, so rendering the two portions independently could prevent the complete pattern from being detected.

## Reusing rendering workspace

Row definitions still have to be lowered to text, rectangles, box-drawing paths, block elements, and mask overlays.
The renderer reuses a `FrameRenderWorkspace` for temporary segment lists and string builders while emitting definitions.
This avoids allocating the same kinds of working collections for every unique row.

Visible rows are exposed as spans and reused inside the inner column loop.
The renderer does not repeatedly call a general cell accessor for every cell when scrollback is not involved.

## Switching rows with SMIL

For each physical row, consecutive frames that reference the same row definition are combined into one interval.
The SVG contains one `<use>` for that interval with an animation such as:

```xml title="output.svg"
<animate
  attributeName="display"
  values="none;inline;none"
  keyTimes="0;0.25;0.5"
  calcMode="discrete"
  dur="4s"
/>
```

`calcMode="discrete"` changes values without interpolation.
That matches a terminal state transition: a row is one state before the boundary and another state after it.

Looping output adds `repeatCount="indefinite"`.
Non-looping output freezes the final animation state instead.
Fade-out is applied to the containing group after the final hold period rather than by altering every row animation.

Text blink is separate from screen-state animation and remains a CSS animation.

## Keeping cursor state separate

Cursor visibility and position are grouped into their own consecutive runs.
A cursor move can therefore change only the cursor definition while the text rows continue to reference the same row content.

This separation also explains why content-frame reduction ignores the cursor.
Cursor timing is preserved at the animation layer without forcing otherwise identical terminal rows to be duplicated.

## Selecting a time range

When a start time is requested, the last state before the range is retained as the initial state if one exists.
The selected timeline is then rebased so that the range begins at zero.

`--sleep` extends the final visible state.
Without an explicit value, the renderer still provides a minimum final hold so that the last state is not removed at the instant it appears.
`--fade-out` begins after that hold.

Full-screen applications commonly leave the alternate screen near process exit.
When the tail consists only of restoring an empty screen, the renderer can trim that restoration so the useful terminal state remains visible at the end.
