---
title: Converting to PNG (resvg)
description: A rasterization path that prioritizes the bundled native resvg library and reduces SVG interpretation differences and startup cost.
---

PNG output requires an implementation that can draw SVG. Automatically launching a browser is powerful, but it brings startup time, browser-version differences, and distribution size into the conversion tool. To keep the stage after SVG generation lightweight, console2svg bundles Rust-based resvg as a small repository-owned C ABI wrapper and chooses it first for PNG conversion.

## Why prefer the bundled version?

The main reason for bundling the resvg host as an independent native asset is to prevent PNG output availability and interpretation from changing depending on whether the user's ffmpeg includes librsvg. resvg warms up the font database once inside the process and reuses the same database for subsequent frames. When converting many frames to PNG, as in video, avoiding an external process launch every time has a large effect.

Default fonts are also ordered by monospaced fonts likely to be available in resvg. Completely identical glyph shapes depend on fonts installed in the OS, but cell width is fixed on the SVG side, limiting the range where renderer differences can move column coordinates.

## Managed/native boundary

`ResvgNative.RenderToPng` calculates only the UTF-8 byte count of the SVG string, encodes it into a buffer borrowed from `ArrayPool<byte>`, and passes it to the native function. The PNG buffer returned by the native side is always released with a dedicated free function after copying. By preventing the caller from guessing ownership, native memory does not accumulate even during long video processing.

Return values distinguish failures in SVG parsing, PNG encoding, drawing, and memory allocation. Each state is converted to a meaningful exception on the .NET side, so an empty PNG is never treated as success.

DLL probing explicitly checks not only next to the executable but also bundled asset directories. This is to find where the native library is actually placed even through portable installs or symbolic links.

When the native library cannot be loaded, automatic selection proceeds to available `rsvg-convert` or SVG-capable ffmpeg. If resvg is explicitly specified, console2svg does not silently switch to another renderer and instead reports that the requested path cannot be used. This boundary prioritizes output reproducibility while still allowing normal usage to select an available converter.
