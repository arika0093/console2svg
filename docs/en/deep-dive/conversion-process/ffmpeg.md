---
title: Converting to video (ffmpeg)
description: The path that rasterizes SVG state sequences into PNG streams and encodes them to video and image formats with ffmpeg.
---

Videos are not simply SVGs concatenated together. Browsers can interpret SMIL, but typical video containers cannot directly contain a timed SVG DOM, so each point in time is converted into a raster frame and passed to ffmpeg as a time series. Because the conversion entry point is unified as SVG, video uses the same themes, chrome, masks, and box-drawing rendering as still SVG.

## Why not create a sequence of temporary files?

The earlier approach of making SVG/PNG numbered files and passing them to ffmpeg increased disk I/O, cleanup, and file locks on Windows in proportion to video length. The current video path generates PNGs in memory and writes them sequentially to `image2pipe` standard input.

Because the file system is not used as a relay point for frames, the temporary area needed until generation finishes and deletion conflicts at exit are reduced.

PNG conversion is CPU intensive, so up to eight conversions are started in parallel according to the number of available processors. However, ffmpeg input must always be in the original order, so frames are sent from a waiting queue to a single standard input in FIFO order. PNGs tend to be larger than SVGs, so the cache for reusing the same SVG result is also limited to 16 entries to prevent long captures from using unlimited memory.

## Do not assume ffmpeg can read SVG

Some ffmpeg builds do not include librsvg for SVG input. The presence of the executable or the display from `-formats` does not necessarily mean SVG can actually be decoded, so before startup console2svg tries an actual conversion from a minimal SVG to PNG and caches the result.

Because a memory pipe cannot separate and interpret multiple concatenated SVGs, the video path always pre-converts to PNG. Automatic selection prefers the bundled resvg first, then SVG-capable ffmpeg, then `rsvg-convert`, and finally an explicit failure. ffmpeg can focus on final encoding, and differences in user environments have a smaller effect on SVG interpretation results.

## Codecs and even dimensions

For MP4, available `libx264` is selected; if unavailable, console2svg falls back to `mpeg4` for Windows Media Player compatibility. WebM and GIF are left to ffmpeg defaults suitable for each container.

Every path specifies `yuv420p` and `pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0`. H.264 4:2:0 requires even width and height, so a 1px shortage caused by font height or padding combinations is added only to the right and bottom edges. This correction satisfies encoding requirements without moving the appearance.
