---
title: PNG Rasterization with resvg
description: In-process invocation of bundled native resvg, font discovery caching, and managed/native memory boundary management.
---

Converting generated SVG vector graphics into PNG format requires rasterization into pixel images.
To avoid heavyweight dependencies on headless browsers (such as Chromium) or external CLI graphics tools, console2svg bundles a C ABI wrapper around **resvg** (a high-performance Rust SVG rendering library renowned for strict SVG specification conformance) directly into its binary distribution.
This enables in-process rasterization without external process invocation, delivering millisecond-level PNG rendering.

## Rationale for In-Process Embedding Over External Commands

Operating systems often provide external utilities such as `rsvg-convert` or headless browser binaries for converting SVG to PNG.
However, console2svg deliberately eschews invoking these tools as external processes, embedding resvg directly into the process via a C ABI wrapper instead.

The primary rationale is **confining the massive disk I/O overhead of operating-system font directory scanning strictly to the initial invocation**.
To render SVG text accurately, an engine must exhaustively scan operating-system font directories (`/usr/share/fonts`, `~/.fonts`, or Windows/macOS system font folders) to index thousands of font files and construct the system font family mapping.
When an external tool is spawned via `Process.Start` for every frame, each invocation initializes a fresh process that rescans thousands of font files from disk from scratch.
This inflicts hundreds of milliseconds of disk I/O latency on every single frame, multiplying across the entire animation or video timeline.

By embedding resvg in-process, the Rust runtime utilizes `OnceLock<Arc<Database>>` to initialize and cache the system font database once in process memory during the initial render.
All subsequent rasterizations—from the second frame onward—reference this in-memory font database directly.
This reduces font-scanning disk I/O to absolute zero, sustaining high-throughput rendering even when generating hundreds of video frames.

Furthermore, console2svg supports explicit font database pre-warming during converter auto-detection, eliminating even the initial frame's discovery latency before rendering begins.

## In-Process Rasterization Workflow

The bundled native library integrates `usvg` for SVG parsing, `tiny-skia` as the 2D software rasterization engine, and `resvg` for layout and drawing.
The pipeline parses incoming SVG markup, paints elements onto a pixel map (pixmap), and encodes the raw pixel buffer into a PNG byte stream returned to the caller.

## Aspect-Ratio Clamping of Output Dimensions

Rasterization image dimensions are calculated from the SVG's intrinsic dimensions (such as `viewBox`) combined with optional width and height flags provided via the CLI.

When both width and height are explicitly specified, those exact values are used.
When only one dimension is supplied, the other is calculated automatically to preserve the SVG's aspect ratio.
When neither is specified, the SVG's intrinsic size is used directly as the pixel resolution.
The computed dimensions are clamped between 1 and 16,384 pixels prior to memory allocation, preventing zero-dimension crashes or accidental allocations of gigabyte-scale pixel buffers from malformed options.

## Buffer Pooling and Safe Native Memory Deallocation

When invoking native functions from .NET, the UTF-8 bytes converted from the SVG string are written into a buffer rented from `ArrayPool<byte>`.
Passing this memory span directly to the native function eliminates intermediate heap allocations.

Conversely, ownership of the rendered PNG byte buffer resides with the Rust runtime.
The .NET wrapper copies the image data from the returned native pointer into a managed `byte[]` array, and guarantees invocation of the native deallocation function (`free`) inside a `finally` block.
This strict boundary design ensures that callers interact purely with managed memory without needing to manage native lifecycles.

## Prioritizing Bundled Assets During Library Resolution

When loading native dynamic libraries (`.so`, `.dylib`, `.dll`), console2svg inspects asset directories adjacent to its own executable before checking standard operating-system dynamic library search paths.

This prioritizes bundled binaries across both standalone release archives (where native libraries sit alongside the main binary) and package manager layouts (where dependencies reside in dedicated asset directories).
Even when console2svg is executed via a symbolic link, it resolves its genuine directory path, avoiding current-working-directory resolution pitfalls.

## Explicit Selection Versus Automatic Fallback

When the converter selection mode (`--svg-converter`) is set to `auto`, console2svg prioritizes the bundled resvg engine whenever available.
If resvg fails to load due to platform constraints, the system attempts an automatic fallback to `rsvg-convert` or an SVG-capable ffmpeg build.

However, when a user explicitly specifies `--svg-converter resvg`, console2svg will never silently switch to another rendering backend if resvg fails to load; it terminates immediately with an error.
This clearly differentiates conversational convenience during automatic detection from strict visual reproducibility under explicit configuration.
