---
title: Generating Animated SVG
description: How terminal state timelines are aggregated into row definitions and emitted as lightweight animated SVGs via discrete SMIL visibility.
---

console2svg's animated SVG avoids storing full-screen image sequences for each frame.
Instead, it feeds recording data sequentially into the terminal emulator, aggregates only modified rows into reusable definitions, and controls row display timing using **SMIL** (Synchronized Multimedia Integration Language: a W3C standard specification for describing animation timing and attribute transitions in XML).
This architecture keeps file sizes and DOM element counts minimal even for lengthy recordings.

## Determining Frame Retention

Event boundaries in terminal recordings do not correspond directly to animation frame boundaries.
A single screen update may arrive split across multiple PTY reads or writes, and certain control sequences alter internal parser state without changing any visible cells.

To resolve this, immediately after processing each event, `ScreenBuffer.GetContentSignature()` computes a hash of the visible screen content and compares it with the preceding frame.
Because this signature excludes cursor position and blink state, cursor movement alone does not trigger new content frames.

In addition, the maximum frame rate configured by `--fps` is enforced.
When multiple display updates occur within a single time window, intermediate states are discarded, and only the latest settled state within that interval is retained as a pending frame.

Snapshots use row-level copy-on-write (CoW).
When a snapshot is created, unmodified row array references are shared with the active buffer, and a row's cell array is cloned into new memory only when subsequent mutations occur on that row.
Consequently, evaluating frames at high frequencies avoids the memory consumption and allocation overhead of deep-copying the complete cell grid.

## Cataloging Unique Rows

Even after all retained frames are determined, console2svg never emits complete screens as SVG elements for every frame.
Terminal displays often maintain identical content across long durations on many rows, such as shell prompts and status bars.

The renderer's `PrepareAnimatedRows` method scans all visible rows across all frames to construct a **row catalog**.
It searches existing rows using each row's visual signature (a hash computed from characters, colors, and attributes).
When a signature matches, it compares all cell data to verify identity, ensuring that hash collisions never cause incorrect row definitions to be reused.
Only unique rows are written to the SVG `<defs>` element.

Simultaneously, necessary character styles (CSS classes) are collected as unique rows are discovered.
Consolidating parsing and definition collection into a single pass eliminates the need for a separate scan over all frames just to collect styles.

## Expressing Fine-Grained Changes with Row Deltas

When only a few characters change within a single row—such as during interactive command typing or a clock's seconds display—**row deltas** are applied.
A row delta references the preceding frame's row definition via a `<use>` element and overlays only the modified column range.

```xml title="Row delta definition example"
<defs>
  <!-- Base row definition -->
  <g id="r1">
    <text y="14" fill="#cdd6f4">Building project... [    ]</text>
  </g>

  <!-- Row definition inheriting r1 and overriding only 4 characters of the progress bar -->
  <g id="r2">
    <use href="#r1"/>
    <!-- Override background and text for modified columns (columns 21-24) -->
    <rect x="176.4" y="0" width="33.6" height="18" fill="#11111b"/>
    <text x="176.4" y="14" fill="#a6e3a1">====</text>
  </g>
</defs>
```

Row deltas are applied selectively.
The delta range is restricted to no more than 16 columns and at most one quarter of the row width, and the nesting depth of `<use>` references is capped below 4 levels.
Dividing deltas too finely increases SVG renderer reference resolution overhead, degrading render performance.

Furthermore, when a delta boundary intersects the right half of a full-width character (continuation cell), the target range expands outward to prevent splitting the character.
When manual mask patterns are specified for a row, row deltas are automatically disabled to prevent secret strings spanning the delta boundary from escaping detection.

## Switching Display Intervals with SMIL

When displaying cataloged row definitions on screen, SMIL `<animate>` elements control their visibility.
Consecutive frames referencing the same row definition on the same physical line are grouped into a single interval (run), represented by a `<use>` element.

Visibility transitions use `calcMode="discrete"`, which eliminates intermediate interpolation.
Terminal displays do not transition smoothly; they switch discretely between character states at distinct moments.

```xml title="Animated SVG row reference structure example"
<svg xmlns="http://www.w3.org/2000/svg" ...>
  <defs>
    <!-- Cataloged unique row definitions -->
    <g id="r1">
      <text y="14" fill="#cdd6f4">$ git commit -m "update"</text>
    </g>
    <g id="r2">
      <text y="14" fill="#a6e3a1">[main 4f1a2b3] update</text>
    </g>
  </defs>

  <!-- Physical line 1: transitions from r1 to r2 over time -->
  <g class="c2s-line" transform="translate(0, 0)">
    <!-- r1 displayed from 0.0s to 2.0s (first 50% of 4s duration) -->
    <use href="#r1">
      <animate
        attributeName="display"
        values="inline;none"
        keyTimes="0;0.5"
        calcMode="discrete"
        dur="4s"
        repeatCount="indefinite"
      />
    </use>

    <!-- r2 displayed from 2.0s to 4.0s (second 50%) -->
    <use href="#r2">
      <animate
        attributeName="display"
        values="none;inline"
        keyTimes="0;0.5"
        calcMode="discrete"
        dur="4s"
        repeatCount="indefinite"
      />
    </use>
  </g>
</svg>
```

Looping animations include `repeatCount="indefinite"`, while non-looping animations freeze their final state with `fill="freeze"`.
End-of-video fade-out effects animate the `opacity` of the top-level parent group rather than altering individual row animations.
Terminal character blinking (SGR 5) is implemented as an independent CSS keyframe animation rather than screen-state switching.

## Cursor Rendering Separated from Content

Cursor position and visibility are rendered in a distinct layer completely decoupled from the row catalog.
If row definitions had to be regenerated whenever the cursor blinks or moves, catalog deduplication would break down.

Emitting cursor timing changes as dedicated animation runs reproduces smooth cursor movement without impairing row caching efficiency.

## Time Axis and Trailing Display Adjustments

When trimming the start time with `--time`, the latest screen state preceding that timestamp is automatically inserted as the initial frame, preventing a blank screen at the start of the trimmed window.

At recording end, the hold time specified by `--sleep` is appended.
Even without an explicit flag, a minimum hold duration is maintained so that the final command output does not immediately vanish into a loop restart.

Full-screen applications such as vim or htop often switch from the alternate screen back to the primary screen upon exit, clearing the display before returning to the shell prompt.
When the final recording event represents this screen restoration and results in an empty display, console2svg discards that restoration event and retains the last useful interactive screen as the final frame.
