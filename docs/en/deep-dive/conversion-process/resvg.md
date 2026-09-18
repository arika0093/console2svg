---
title: Rasterizing SVG with resvg
description: How the bundled native resvg path turns SVG into PNG while reusing font state and controlling managed/native memory ownership.
---

PNG output needs an SVG renderer.
console2svg bundles a small Rust C ABI wrapper around resvg so the normal PNG path does not depend on a browser process or on whether the installed ffmpeg was built with SVG support.

## Keep rasterization inside the process

The native wrapper parses SVG with usvg, renders it through resvg and tiny-skia, and encodes the resulting pixmap as PNG.

System-font discovery is process-wide state.
A `OnceLock<Arc<Database>>` initializes the font database on first use and later renders clone the shared reference instead of scanning installed fonts for every frame.
The .NET side can explicitly warm that database during converter detection so video rendering does not pay the discovery cost on an arbitrary later frame.

This matters most for video, where hundreds of SVG states may pass through the same renderer.

## Preserve output dimensions deliberately

The native wrapper reads the SVG's intrinsic size and applies optional raster dimensions.

When both width and height are supplied, those values are used.
When only one dimension is supplied, the other is derived from the SVG aspect ratio.
When neither is supplied, the SVG size is used directly.

The resulting dimensions are clamped to 1 through 16,384 pixels before a tiny-skia pixmap is allocated.
This prevents invalid zero dimensions and bounds an accidental request for an extreme raster surface at the native boundary.

## Reuse the managed input buffer

`ResvgNative.RenderToPng` first computes the UTF-8 byte count of the SVG string.
It rents a byte array from `ArrayPool<byte>`, encodes the SVG into that array, invokes the native function, and returns the rented array afterward.

The native renderer owns the PNG buffer it returns.
The .NET wrapper copies that PNG into a managed byte array and calls the matching native free function in a `finally` block.
The ownership rule is explicit: the caller receives managed PNG bytes and never has to infer how Rust allocated the native buffer.

Native status codes distinguish SVG parse failure, PNG encoding failure, render failure, and allocation failure.
The managed wrapper converts them into exceptions rather than treating an empty or partial buffer as successful output.

## Resolve the bundled library before system locations

The native-library resolver checks console2svg's bundled asset directories before falling back to ordinary loader resolution.
This covers release layouts where the executable and native library are colocated as well as package layouts where native assets live in a sibling library directory.

The explicit search is also useful for portable installations and paths reached through symbolic links, where relying only on the process working directory would be fragile.

## Fall back only when the mode allows it

Automatic converter selection prefers the bundled resvg path when it is available.
Other image-conversion paths can fall back to `rsvg-convert` or an ffmpeg build that has been proven to decode SVG.

An explicitly requested resvg mode does not silently change renderers when the native library fails to load.
That distinction makes a user-selected renderer reproducible while still allowing automatic mode to select an available implementation.
