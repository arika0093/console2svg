---
title: Conversion Pipeline Overview
description: End-to-end processing architecture from terminal byte capture to emulation, SVG structuring, animation, and video/image rasterization.
---

console2svg is far more than a basic screenshot utility that wraps terminal text in an image container.
It launches child processes through OS-level pseudo-terminals (PTYs), processes raw output byte streams through an in-memory terminal emulator to maintain an accurate virtual display, and converts the resulting buffer into highly optimized SVG vectors, raster images, video streams, and real-time web broadcasts.

To achieve maximum visual fidelity alongside aggressive deduplication, console2svg decomposes the transformation into clearly demarcated pipeline stages:

```text
[Child Process Launch / Replay Input]
         │ Raw VT byte stream
         ▼
[Stage 1: Input Capture and Stream Control]
         │ Coalesced event stream
         ▼
[Stage 2: Terminal Emulation and Sequence Parsing]
         │ Virtual Screen Buffer (ScreenBuffer)
         ▼
[Stage 3: Secret Detection and Redaction]
         │ Masked cell grid
         ▼
[Stage 4: SVG Layer Structuring and Optimization]
         │ Optimized vector markup
         ├─► [Stage 5A: Still Image / Animated SVG]
         ├─► [Stage 5B: Fast Rasterization / Video Encoding]
         └─► [Stage 5C: Real-Time Web Broadcast]
```

## Pipeline Stages

The transformation process spans five foundational stages:

### Stage 1: Input Capture and Stream Control

Accurate terminal reproduction begins by capturing raw child process output without byte corruption or timing jitter.

* **[PTY Launch, Control, and Stream Capture](./pty.md)**: Bridges Windows ConPTY and Unix PTY implementations, guarantees multibyte UTF-8 boundary integrity, and coalesces closely spaced output events to minimize parser overhead.
* **[Deterministic Input Replay](./replay.md)**: Normalizes recorded keystroke timestamps and synthesizes authentic VT escape byte sequences to recreate terminal interactions deterministically.

### Stage 2: Terminal Emulation and Sequence Parsing

Raw terminal streams contain ANSI control codes for cursor repositioning, buffer clearing, and SGR attribute styling rather than flat text.

* **[Escape Sequence Parsing and Buffer Updates](./sequence-parsing.md)**: Employs a stateful parsing automaton to decode CSI, OSC, and SGR sequences, handling full-width character column widths, margins, and cursor state within a two-dimensional `ScreenBuffer`.
* **[Special Characters, Box-Drawing, and Blocks](./special-fragments.md)**: Replaces font-dependent box-drawing characters and block elements with crisp vector geometry paths, smoothing junction seams and rounded corners.

### Stage 3: Secret Detection and Redaction

When capturing terminal sessions for documentation or public video, sensitive credentials must never leak into visual output.

* **[QuickLeaks Secret Detection and Redaction](./quickleaks.md)**: Accelerates regex scanning using precompiled literal anchors and maps detected character bounds across two coordinate planes to excise confidential tokens directly from SVG text nodes.

### Stage 4: SVG Layer Structuring and Optimization

Converting virtual screen cells into web-standard SVG requires structural optimization to prevent massive document inflation.

* **[SVG Layer Hierarchy and Coordinate Systems](./layer-structure.md)**: Separates outer margins, window chrome, drop shadows, background fills, and foreground text into distinct layers under a unified character grid coordinate system.
* **[Pipeline Optimizations](./optimization.md)**: Leverages copy-on-write buffer snapshotting, XxHash3 row signatures, and horizontal span merging for rects and paths to achieve extreme file size reductions.

### Stage 5: Target Output Generation

The structured, optimized screen representation is finally dispatched into specific export formats according to CLI commands:

* **[SMIL Animated SVG Generation](./animation.md)**: Indexes distinct rows into a `<defs>` catalog and applies discrete SMIL `<animate>` visibility switching alongside partial row deltas to produce compact animated SVGs.
* **[PNG Rasterization via resvg](./resvg.md)**: Embeds the Rust-based resvg rendering engine in-process via C ABI, caching OS font scans across runs to render PNG frames in milliseconds.
* **[Video Encoding via ffmpeg Pipeline](./ffmpeg.md)**: Powers deterministic video export through unidirectional emulation advances, a two-stage memory cache, multithreaded rasterization, and direct `image2pipe` streaming to ffmpeg.
* **[live-server Streaming Architecture](./live-server.md)**: Runs an embedded HTTP/SSE server featuring three-layer streaming, update settle delays, and client-side rAF coalescing for real-time terminal mirroring in web browsers.

---

For architectural details, algorithms, and design choices, explore the linked articles above.
