---
title: Design philosophy
description: The design philosophy of console2svg.
---

console2svg is a CLI tool designed to convert terminal output into accurate, high-quality vector images (SVG) and videos.

## Design philosophy

console2svg is developed based on the following design principles.

### Works everywhere

I am a Windows user. (Well, most people probably are.)  
Honestly, I am tired of tools that only document `brew` installation.  

Therefore, console2svg aims to run on the major Linux/macOS/Windows platforms.


### Works out of the box

One motivation for developing console2svg was that this kind of conversion tool often requires advanced combinations of many different pieces, which was very tedious.  
console2svg aims to be usable immediately after installation.

* In Linux/macOS environments, all dependencies except `ffmpeg` are built into the binary.
* In Windows environments, everything including `ffmpeg` is bundled in the release archive.
  * The bundled binary uses [btbN/FFmpeg-Builds](https://github.com/btbN/FFmpeg-Builds).
  * This is because separately installing and configuring the path for `ffmpeg` on Windows is troublesome.

### Does not make users work harder

console2svg emphasizes not making users do unnecessary work.  
Examples include features such as [interactive capture](../basic-usage/interactive-capture.md) and [tmux support](../utilities/tmux.md).

> [!TIP]
> If you have ideas, please open an [Issue](https://github.com/arika0093/console2svg/issues)!


### High-quality output

console2svg aims to reproduce terminal output as faithfully as possible.  

> [!NOTE]
> That said, perfect reproduction still has many challenges.  
> If you find display problems, please report them in an [Issue](https://github.com/arika0093/console2svg/issues).

### Minimize package dependencies

To improve maintainability, console2svg aims to use as few [packages](./license.md) as possible.
