---
title: File formats and embedded metadata
description: Understand replay, cast, theme, and SVG metadata formats.
---

console2svg works with several file formats, each serving a different purpose. Keep the generated image separate from the source recording when you want an artifact that is easy to embed but do not want to disclose the underlying session.

## SVG output

SVG is the native output format. It contains terminal text, styles, optional window chrome, backgrounds, and animation timing. An SVG can be embedded in Markdown or viewed directly in a browser.

## Replay files

Replay files are JSON documents containing recorded keyboard input and timing information. They can be rendered again with different visual options.

## Cast files

Asciicast-compatible cast files contain timed terminal output events and metadata. They are useful for exchanging recordings with terminal-recording tools.

## Theme manifests

Theme manifests define palette colors and terminal defaults for reusable themes.

## Embedded metadata

Captures can optionally embed source information such as cast data, replay input, or verbose diagnostic logs in SVG metadata.

> [!CAUTION]
> Embedded metadata travels with the SVG. Do not publish an image with embedded input or logs unless you have reviewed that information for secrets and local paths.

## Rendered SVG vs. embedded metadata

```text
+---------------------+      +---------------------------+
| replay.json         |      | console.cast              |
| (keyboard + timing) |      | (timed output + metadata) |
+----------+----------+      +-------------+-------------+
           |                               |
           v                               v
+--------------------------------------------------+
| console2svg capture / replay / convert           |
+------------------------+-------------------------+
                         |
                         v
+--------------------------------------------------+
| output.svg (rendered)                            |
|  - terminal text, styles, window chrome          |
|  - backgrounds, animation timing                 |
+--------------------------------------------------+
                         |
        +----------------+----------------+
        |                                 |
        v                                 v
+------------------+            +-------------------+
| plain artifact   |            | with embedded     |
| (default)        |            | --embed-replay /  |
| shareable image  |            | --embed-cast /    |
| only             |            | --embed-log       |
+------------------+            +-------------------+
```

Use the left path for docs and blog images. Use the right path only when another machine must re-render the exact session with different visual options.
