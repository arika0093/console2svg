---
title: Video Encoding with ffmpeg
description: How terminal state sampling, PNG rasterization, and pipelining into a single ffmpeg process are coordinated.
---

When exporting to video formats such as MP4 or WebM, console2svg uses the same terminal emulator and SVG renderer as it does for still images.
At each specified sampling interval, terminal states are generated as static SVGs, rasterized to PNG, and streamed sequentially into the standard input of **ffmpeg** (an open-source video and audio processing tool).
Completing the entire pipeline in memory without intermediate files eliminates disk I/O overhead.

## Unidirectional Replay in the Terminal Emulator

When sampling video frames at a fixed frame rate (FPS), the emulator is never restarted from time zero for each successive frame.
Instead, a single emulator instance is maintained, and only newly reached, unprocessed events are applied forward as the sampling timestamp advances.

The event lookup index also moves strictly forward.
Eliminating repetitive scans over past events keeps the total processing time strictly linear relative to recording duration.

## Suppressing SVG Regeneration with Signatures

Immediately after advancing the emulator to the sample timestamp, console2svg retrieves the visual signature of the entire screen (a hash of cell contents and cursor state).
Using this signature as a key, up to 128 recently generated SVG strings are cached.

Because CLI applications spend substantial time idling while awaiting user keystrokes or command completion, many consecutive samples point to identical screens.
When signatures match, XML construction is bypassed completely, and the identical `string` instance is returned from the cache.

## In-Order Parallel Rasterization

Rasterizing SVG to PNG is a CPU-bound, computationally heavy operation.
To maximize throughput, rasterization executes in parallel across up to 8 threads depending on available CPU cores.

At the same time, the chronological sequence of image frames passed to the video encoder must be strictly preserved.
To achieve this, asynchronous rasterization tasks are queued in a capacity-bounded FIFO buffer, and a single writer pipes completed frames to ffmpeg's standard input in order of task completion at the head of the queue.
This mechanism harnesses multi-core parallelism while preventing the pipeline from accumulating an entire video's uncompressed frames in memory.

## Two-Stage Caching via Object Identity

Because rendered PNG images have a much larger memory footprint than SVG strings, the PNG cache is capped at 16 entries.

Rather than performing string comparisons on the SVG, this cache uses object reference identity as its key.
This works seamlessly because the preceding SVG cache returns the exact same `string` instance for identical screens.
As a result, console2svg realizes a **two-stage cache** that skips both SVG regeneration and PNG re-rasterization without needing to compute hashes or perform full-text comparisons on large XML strings.

## Single-Process Streaming via image2pipe

Generating a complete video requires launching only a single ffmpeg process.
All image frames are passed using **image2pipe** (an ffmpeg input format that ingests a continuous image stream via standard input or a pipe).
The process is invoked with `-f image2pipe -vcodec png -i pipe:0`.

Both standard output and standard error from the child process are drained asynchronously from the moment the process starts.
This prevents pipe buffers from overflowing and causing ffmpeg to deadlock, while preserving error diagnostics emitted to stderr if encoding fails.
If processing is cancelled, a termination signal is dispatched across the entire process tree to cleanly reclaim resources.

## Pre-Flight Probing for SVG Decoders

Different ffmpeg builds vary widely in their bundled libraries.
Even when SVG is listed among supported formats, some environments lack the internal SVG decoder (such as libxml2 or librsvg) required for rasterization.

To prevent runtime failures, console2svg performs an upfront probe converting a minimal SVG to PNG, verifying whether SVG decoding functions properly and caching the outcome.
In the in-memory video pipeline, because concatenated multi-frame SVGs cannot be reliably delimited over a raw pipe, the pipeline prioritizes the bundled resvg library or `rsvg-convert` for rasterization, allowing ffmpeg to focus exclusively on video encoding.

## MP4 Codec Selection and Even-Dimension Padding

When outputting to an MP4 container, console2svg prefers `libx264` if available on the system, falling back to `mpeg4` otherwise.
WebM and GIF outputs delegate codec selection to the container defaults.

For MP4 pixel formatting, console2svg specifies **yuv420p** (a widely compatible format that separates luminance Y from chrominance U/V and subsamples chroma every 2x2 pixels).
Because the yuv420p specification mandates even image widths and heights, the following ffmpeg filter is applied:

```text
pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0
```

This filter rounds odd pixel widths or heights up to the nearest even integer, appending at most one pixel of padding along the right or bottom edge.
This satisfies encoder constraints without shifting the top-left origin coordinates of the terminal content.
