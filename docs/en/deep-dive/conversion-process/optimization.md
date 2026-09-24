---
title: Optimizing the Conversion Pipeline
description: Multi-layered optimizations spanning input event coalescing, memory reuse, row cataloging, and SVG structural compression.
---

Terminal recordings inherently contain duplicate information across multiple processing stages.
A single screen update is frequently fragmented across multiple PTY (pseudoterminal) reads, the vast majority of character cells remain unchanged between adjacent frames, identical rows persist for seconds at a time in animations, and video conversion repeatedly requests rasterization for static screens.
console2svg systematically eliminates these redundancies across each pipeline stage before invoking downstream rendering engines.

## Regulating Event Arrival with Coalescing

Byte sequences read from a PTY are never recorded directly as individual recording events.
Instead, console2svg applies **output coalescing** (grouping consecutive I/O chunks that arrive within a narrow time window into a single batch), pruning the recorded event count upfront.

The default coalescing window is set to one quarter of the video frame interval.
To preserve interactive responsiveness, it is never reduced below 2 milliseconds, and to prevent perceived lag, it is capped at 20 milliseconds.
This preprocessing step dramatically reduces the number of event boundaries the ANSI parser must interpret, even when dealing with complex TUI (Text User Interface) applications that redraw screens using streams of fragmented escape sequences.

## Sharing Screen Snapshots via Copy-on-Write

If every retained animation frame required a deep copy of the entire screen grid, memory consumption would explode proportionally to `frames × rows × columns`.
To eliminate this overhead, `ScreenBuffer` employs **copy-on-write** (sharing memory until a write occurs, cloning only the mutated portion).

When taking a snapshot, only the outer array holding row references is newly allocated; the underlying row data (`ScreenCell[]`) is shared directly with the live screen buffer.
Only when subsequent terminal emulation mutates characters on a specific row is that individual row's cell array cloned into fresh memory.

This row-sharing architecture also accelerates frame comparisons.
When two snapshots reference the identical row array instance, `HasSameVisualRow` identifies the row as completely identical immediately, without inspecting individual cells.
Cell-by-cell data comparisons are restricted strictly to rows with differing array references.

## Rapid Visual Signature Calculation via Differential Updates

console2svg uses **visual signatures** to determine whether screens or rows are identical.
A cell signature is computed from its character glyph, style attributes, and wide-character flags.
Hashing is powered by high-performance **XxHash3**, with precomputed lookup tables for ASCII characters (0–127).

Each row maintains a 64-bit row signature derived from the set of positioned cell signatures.
Once signature tracking begins, modifying a single character cell does not require rescanning the entire row.
The row signature is updated incrementally by removing the previous cell's value via XOR and adding the new cell's value via XOR.

The system computes two types of full-screen signatures:
`GetContentSignature` excludes the cursor and determines whether an animation content frame needs to be retained.
`GetVisualSignature` incorporates cursor position and visibility, ensuring exact rendering equivalence during video frame sampling.

## Reusing String and Style Objects

Terminal output frequently repeats the exact same characters and styling attributes across expansive regions.
console2svg preallocates static single-character `string` instances for all 128 ASCII code points, completely eliminating heap allocations from `char.ToString()` during cell updates.

`CellStyle` instances representing text attributes are likewise interned and shared across the buffer.
A fast path directly reuses the previous cell's style instance during common consecutive runs.
When identical styles reappear across disparate screen locations, references are retrieved from an internal cache capped at 256 entries.
Even when continuously processing novel 24-bit Truecolor streams, the cache automatically rebuilds itself upon reaching its limit to prevent unbounded memory growth.

## Consolidating SVG Elements and Text Placement

When serializing to SVG, console2svg avoids naive per-cell markup.

Contiguous horizontal cells sharing the same background color are merged into a single `<rect>`.
Similarly, adjacent foreground cells sharing identical styles are concatenated into a single `<text>` element.
Whitespace within a line is buffered and emitted only when followed by subsequent visible text, while trailing blank cells at the end of a line are trimmed entirely.
When full-width characters or geometric shapes appear, text runs are segmented cleanly at those boundaries to preserve precise column alignment.

```xml title="Consolidating adjacent character cells"
<!-- Before optimization (naive per-cell rendering): individual rect and text per cell (6 elements, redundant attributes) -->
<rect x="0" y="0" width="8.4" height="18" fill="#1e1e2e"/>
<text x="0" y="14" fill="#89b4fa">g</text>
<rect x="8.4" y="0" width="8.4" height="18" fill="#1e1e2e"/>
<text x="8.4" y="14" fill="#89b4fa">i</text>
<rect x="16.8" y="0" width="8.4" height="18" fill="#1e1e2e"/>
<text x="16.8" y="14" fill="#89b4fa">t</text>

<!-- After optimization (console2svg consolidation): merged into single background and text elements -->
<rect x="0" y="0" width="25.2" height="18" fill="#1e1e2e"/>
<text x="0" y="14" fill="#89b4fa">git</text>
```

Furthermore, box-drawing characters are decomposed into horizontal and vertical line segments prior to SVG serialization.
Segments sharing identical colors and stroke widths that connect continuously are merged into a single `<path>` element.
Even in intricate TUI tools covered in borders, hundreds of individual rectangles or glyph fragments are collapsed into just a handful of continuous paths.

```xml title="Consolidating box-drawing paths"
<!-- Before optimization: numerous tiny fragments emitted for each line intersection and segment -->
<!-- After optimization: outer borders and dividers sharing the same style unified into one path -->
<path d="M 10 10 H 630 V 200 H 10 Z M 10 40 H 630" stroke="#6c7086" stroke-width="1" fill="none"/>
```

## Deduplication and Delta Encoding for Animated Rows

Animated SVGs never output complete screens for every retained frame.
Instead, `PrepareAnimatedRows` scans visible rows across all frames to construct a **row catalog**, defining identical row content only once under `<defs>`.

In addition, for typing or status updates where only a small portion of a line changes, console2svg applies **row deltas** that reference the previous row definition and overwrite only the altered column range.
By capping delta ranges to 16 columns or less and at most one quarter of total row width, and limiting `<use>` chain depth to under 4 levels, output size is minimized while avoiding resolution bottlenecks in SVG renderers.

For example, consider a 100-frame animation of command typing in an 80×24 terminal:

| Technique | Emitted Row Data | Approximate File Size |
| :--- | :--- | :--- |
| **Naive full-screen expansion** (emits every row for every frame) | 2,400 rows (full 80×24×100 data) | ~3.2 MB |
| **Row cataloging** (deduplicates and references unique rows) | ~120 unique `<g>` definitions | ~180 KB |
| **Row cataloging + row deltas** (partially overwrites typing changes) | ~35 complete definitions + ~40 micro-deltas | ~85 KB |

Through this multi-tiered deduplication, file sizes are reduced by over 90% without sacrificing visual fidelity.

## Two-Stage Caching and Pipelined Video Streaming

When generating video at a fixed frame rate, a single terminal emulator instance advances forward as the sampling timestamp progresses, computing the visual signature at each step.

Based on this signature, up to 128 rendered static SVG strings are cached.
When consecutive samples display identical screens, XML construction is bypassed and the identical `string` instance is returned.
The video converter then uses this `string` object reference as a key to cache up to 16 PNG rasterization results.
This **two-stage cache** completely eliminates both SVG regeneration and PNG re-rasterization during periods where the screen remains static.

Completed PNG frames are streamed directly into the standard input (`image2pipe`) of a running ffmpeg process without writing intermediate files to disk.
Rasterization runs concurrently across up to 8 threads based on CPU core count, and a capacity-bounded FIFO queue regulates transfer to preserve strictly sequential frame delivery to the encoder.
