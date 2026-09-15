---
title: Converting output formats
description: Convert captures to PNG, GIF, MP4, and other formats.
---

SVG is console2svg's native output. The extension supplied to `-o` selects a conversion target, so the same capture command can produce an image or video artifact when a consumer cannot embed SVG.

```bash
console2svg capture -o output.png -- dotnet --version
console2svg capture -o output.gif -v -- cmatrix -ab
console2svg capture -o output.mp4 -v -- cmatrix -ab
```

Static formats such as PNG and JPEG capture one terminal state. Animated GIF, WebM, and MP4 outputs need video mode (`-v`).

## Dependencies

The Windows release archive includes FFmpeg. On Linux, install FFmpeg with the distribution package manager; on macOS, Homebrew is a common choice. The bundled resvg converter is used where available, while FFmpeg and librsvg provide conversion paths depending on platform and output format.

When a conversion fails, run with verbose logging and check the [status and verbose logs](/reference/status-and-verbose-logs/) page.

![An animated capture converted to a GIF](/assets/cmd-matrix-video.gif)

<!-- TODO: Add a small static PNG output example next to the animated GIF. -->
