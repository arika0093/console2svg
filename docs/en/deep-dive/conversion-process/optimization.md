---
title: Optimizing the conversion pipeline
description: How console2svg reduces parsing work, frame copies, SVG DOM size, allocations, rasterization work, and file-system I/O.
---

Terminal recordings contain duplication at several levels.
Many PTY reads belong to one visual update, most cells are unchanged between adjacent states, many rows repeat across frames, and video sampling often asks for the same screen more than once.
console2svg removes that duplication before paying for later stages.

## Reduce work before rendering

PTY output is not stored as one event per read by default.
Small output chunks are coalesced before they enter `RecordingSession`.
The default coalescing window is one quarter of the target frame interval, clamped to 2 through 20 milliseconds, and a batch is not allowed to grow beyond one frame interval.
This reduces the number of parser calls and candidate visual states produced by applications that redraw a screen with many small writes.

Animated replay applies a second reduction after terminal emulation.
`TerminalEmulator.ReplayFrames` compares content signatures and ignores events that do not change visible cell content.
When a positive FPS limit is configured, updates inside one frame interval are represented by the latest pending state rather than by every intermediate write.
The first and final states are still retained.

Static rendering follows a different path because it normally needs only one screen.
The renderer advances one emulator to the requested or final useful state instead of taking a `ScreenBuffer` snapshot after every event.
This avoids paying the animation snapshot cost for a still image.

## Make screen snapshots cheap

Animation needs retained terminal states, but copying the complete cell matrix for every state would scale with `frames × rows × columns`.
`ScreenBuffer` therefore uses copy-on-write row storage.

A visible snapshot copies the outer row-reference array and shares the underlying row arrays.
When the live screen later modifies a shared row, only that row is cloned.
Unchanged rows remain shared between snapshots.
A pending frame inside the FPS window can also replace its visible state by copying row references instead of rebuilding a deep snapshot.

Row sharing helps comparison as well as memory use.
When two snapshots point to the same row array, `HasSameVisualRow` can accept the row immediately.
Only rows with different references need cell-by-cell comparison.

## Maintain signatures incrementally

Each cell has a visual signature derived from its text, effective style, and wide-character flags.
Styles and strings are hashed with `XxHash3`, and signatures for single-byte ASCII text are cached.

Each visible row has a signature built from its positioned cell signatures.
After signature tracking starts, changing one cell updates the row signature by removing the old positioned-cell value and adding the new one with XOR.
A one-character update therefore does not require rescanning the full row.

A screen signature is computed from the row signatures.
`GetContentSignature` excludes the cursor and is used when deciding whether terminal content warrants another retained animation frame.
`GetVisualSignature` includes cursor visibility and position and is used when exact rendered output must be distinguished.

The temporary byte span used to combine row signatures is stack allocated up to 4096 bytes.
Larger screens use `ArrayPool<byte>` instead of allocating an exact-size array on every call.

## Reuse common terminal objects

Terminal output is dominated by repeated ASCII characters and repeated text styles.
console2svg precomputes one-character strings for ASCII code points and reuses those references instead of calling `char.ToString()` for every cell update.

`CellStyle` instances are interned inside `ScreenBuffer`.
A last-style fast path handles long runs that keep the same SGR state, while a bounded dictionary handles less-local reuse.
The dictionary is cleared after it grows beyond 256 entries so an input stream that continuously invents RGB combinations cannot make the cache grow without limit.

Hot dictionary paths use `CollectionsMarshal.GetValueRefOrAddDefault` where appropriate.
This avoids a separate lookup for “find” followed by another lookup for “insert”.

## Compact the SVG representation

The SVG renderer does not emit one element per cell.

Adjacent cells with the same non-default background are emitted as a single rectangle.
Adjacent foreground cells with the same effective text style are combined into one `<text>` element.
Interior spaces are buffered and retained only when they connect to later text in the same run; trailing blank cells are omitted.
Wide characters and geometry-rendered characters terminate a text run so that column accounting remains exact.

Repeated text styles receive short CSS class names through `SvgStyleRegistry` instead of repeating style attributes.
Repeated rectangle and path descriptions inside reusable definitions are assigned an ID by `SvgElementRegistry`; later occurrences use `<use>`.

Box-drawing characters are converted to horizontal and vertical segments before serialization.
Compatible adjacent segments are merged, then rectangles with the same color are collected into compact paths.
A benchmark recorded when this optimization was introduced reduced the btop SVG used in that change from 68.5 KB to 31.3 KB and reduced box-path elements from 417 to 5.
Those numbers describe that workload and revision, not a fixed ratio for arbitrary terminal output.

`SvgWriter` also avoids transient strings on numeric hot paths.
Integers and floating-point values are formatted into stack buffers with `TryFormat` and written as spans.
A `StringBuilder` is written through `GetChunks()` instead of first materializing another full string.

## Deduplicate animated rows

Animated SVG does not store a complete SVG frame for every retained terminal state.
`PrepareAnimatedRows` creates a catalog of unique row definitions.

The first check uses the row visual signature.
Rows with the same signature are then compared using the actual cells, so a hash collision cannot silently substitute the wrong row.
Only a newly discovered row is scanned for text styles; style collection and unique-row discovery therefore share the same pass.

A row that differs only in a small area may be represented as a delta over its previous definition.
The delta is used only when the changed range is at most 16 columns, at most one quarter of the visible width, and the chain depth remains below four.
These limits keep `<use>` chains shallow instead of trading file size for an expensive dependency tree.
Wide-character boundaries expand the changed range when necessary.

Manual mask patterns disable row deltas.
A pattern can cross the boundary between unchanged base content and a changed fragment, so splitting the row would remove the complete string context needed for matching.

Rendering the row catalog reuses a `FrameRenderWorkspace`.
Lists for line segments, corners, block rectangles, and merged rectangles, together with string builders for foreground text, normalized mask text, and path data, are cleared and reused instead of allocated for every row definition.

## Emit animation by row runs

After row definitions are deduplicated, consecutive frames that use the same definition for a physical row are grouped into one run.
One `<use>` element represents the run, and a discrete SMIL `display` animation defines when that row is active.
Cursor states are grouped separately, so cursor-only movement does not duplicate row content.

This changes the dominant unit from “number of frames times number of rows” to “number of row-content transitions”.
Long recordings with a mostly stable screen benefit most from that distinction.

## Avoid repeated rasterization in video output

Fixed-FPS video sampling advances one `TerminalEmulator` monotonically through the recording.
It does not replay event zero through the target event for every sample.
The event pointer also moves only forward as sample time increases.

Rendered frame SVGs are cached by visual signature, with at most 128 entries.
When a sampled screen repeats, the same SVG string object is returned.

The video converter uses that object identity as the key to a smaller PNG render cache.
Repeated visual states can therefore reuse the same SVG object and the same PNG-render task without hashing or comparing a large SVG string.
The PNG cache is capped at 16 entries because raster frames can consume substantially more memory than their SVG source.

SVG-to-PNG work may run concurrently, with parallelism limited to the processor count and capped at eight.
Completed PNGs are still written to ffmpeg through a bounded FIFO queue in frame order.
This gives rasterization some CPU parallelism without allowing an entire long recording to accumulate as PNG byte arrays.

## Keep frame transport in memory

Video encoding uses one ffmpeg process for the output.
PNG frames are written directly to its `image2pipe` standard input instead of being written as temporary numbered files and read back.

The bundled resvg path is also in-process.
Its system font database is initialized once and shared by later renders.
Managed SVG text is encoded into a buffer rented from `ArrayPool<byte>` before crossing the native boundary.

Together, these choices remove repeated process startup and most per-frame file-system operations from the normal video path.

## Measure the individual stages

The benchmark project separates terminal replay, production frame preparation, unique-row catalog construction, row-definition emission, SMIL emission, static rendering, animated rendering, and real asciicast workloads.
It also measures automatic masking at several FPS values.

`MemoryDiagnoser` records managed allocations and collections.
On Linux systems with a usable `perf` installation, the benchmark configuration can also collect retired instructions, cycles, branches, cache misses, CPU samples, and deeper disassembly of the render-to-buffer call chain.

This split matters because an optimization that reduces SVG bytes may not reduce generation time, and an allocation optimization may not change the serialized SVG at all.
