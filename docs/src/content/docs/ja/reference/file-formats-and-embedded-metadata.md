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

<!-- TODO: Add a diagram that distinguishes a rendered SVG from optional embedded replay, cast, and log metadata. -->
