---
title: Encoding video with ffmpeg
description: How terminal states are sampled, rasterized to PNG in parallel, and streamed in order through one ffmpeg image2pipe process.
---

Video output uses the same SVG renderer as still images.
Each sampled terminal state is first represented as a static SVG, rasterized to PNG, and then sent to ffmpeg as an ordered image sequence.

The normal in-memory path does not ask ffmpeg to split concatenated SVG documents.
`image2pipe` receives PNG frames because individual PNG images have boundaries the demuxer can consume from a byte stream.

## Advance one emulator through sampled time

Fixed-FPS sampling does not replay the recording from event zero for every video frame.

The frame generator owns one `TerminalEmulator` and a monotonically increasing event index.
As sample time moves forward, only newly reached events are processed.
The event lookup index also moves forward rather than searching the recording again for every frame.

This keeps terminal replay work proportional to the recording event stream instead of multiplying earlier events by the number of later samples.

## Cache repeated SVG states

After advancing the emulator, the generator reads the screen visual signature.

Rendered static SVG is cached by that signature with a maximum of 128 entries.
If several samples show the same terminal state, the generator returns the same cached SVG string object rather than rebuilding equivalent XML.

Without an explicit FPS, saved frame sequences also skip consecutive states with the same visual signature.

## Rasterize concurrently but write in order

SVG-to-PNG conversion is CPU or process bound, depending on the selected rasterizer.
The video converter starts several PNG renders concurrently.
Parallelism follows the available processor count and is capped at eight.

The frames still have a strict order.
Pending render tasks are held in a bounded FIFO queue, and only the task at the front is written to ffmpeg standard input.
This gives independent rasterization work overlap while keeping one ordered pipe writer.

The queue bound also prevents a long recording from producing all PNG byte arrays before ffmpeg consumes them.

## Reuse PNG renders by SVG object identity

PNG output can be much larger than its source SVG, so the raster cache is deliberately smaller than the SVG cache.
It holds at most 16 entries.

The cache uses reference equality for the SVG string key.
That works together with the previous stage: a repeated visual signature returns the exact same SVG string object.
The converter can therefore reuse the same PNG-render task without hashing or comparing the contents of a large XML string.

This is a two-stage cache.
Screen identity first prevents repeated SVG construction, then SVG object identity prevents repeated rasterization.

## Feed one ffmpeg process

console2svg starts one ffmpeg process for the complete video and writes every PNG frame to its standard input.

The input arguments use `-f image2pipe -vcodec png -i pipe:0` with the requested frame rate.
No numbered SVG or PNG files are required on the normal in-memory path.

Standard output and standard error are drained asynchronously from process start.
This prevents a full child-process pipe from blocking encoding and preserves ffmpeg diagnostics for a failure.
Cancellation attempts to kill the complete ffmpeg process tree.

After the last PNG is written, standard input is closed and console2svg waits for the encoder to exit.

## Probe capabilities instead of trusting format listings

An ffmpeg executable can advertise an SVG pipe format even when its build lacks the decoder needed to rasterize SVG.
For image conversion, console2svg therefore probes SVG support with a real minimal SVG-to-PNG conversion and caches the result.

The in-memory video path has a different constraint.
When the selected converter mode points to ffmpeg, the video pipeline resolves a PNG-capable renderer such as bundled resvg or `rsvg-convert` first, because the final ffmpeg process is already being used as the video encoder and cannot separate concatenated SVG documents on `image2pipe`.

Codec discovery is cached as well so repeated conversions do not invoke `ffmpeg -encoders` for every selection.

## Choose compatible MP4 output

For MP4, console2svg prefers `libx264` when the encoder is available and falls back to `mpeg4`.
Other containers such as WebM and GIF are allowed to use ffmpeg's container-appropriate default encoder when no explicit MP4 codec is selected.

The output uses `yuv420p` and applies:

```text
pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0
```

The padding rounds odd raster dimensions up to even values.
At most one pixel is added on the right or bottom, so the terminal content keeps its original top-left position while satisfying formats that require even chroma dimensions.
