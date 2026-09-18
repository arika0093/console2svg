---
title: Optimizing SVG output
description: How DOM count, string size, and generation-time allocations are reduced while preserving cell-grid fidelity.
---

A terminal is essentially a cell-based screen, but SVG does not need to be output as one DOM element per cell. If an 80×24 screen becomes one `<text>` per character, even short output becomes thousands of elements, making not only file size but also DOM construction and layout on the viewer side heavy. Optimization is not a process for approximating the display; it is a process that keeps the meaning of each cell while making the representation unit coarser.

## Why group text and backgrounds separately?

Backgrounds are emitted as one `<rect>` for horizontal ranges with the same effective background color. Inverse display is judged using the colors after swapping foreground and background, and cells matching the theme's default background are left to the parent's base background. Adjacent rectangles receive a thin, non-scaling stroke of the same color. This eliminates subpixel seams that appear during reduced display; it is not meant to make cell boundaries themselves look thicker.

Text is also combined into one `<text>` for consecutive cells with the same color, bold, italic, underline, strikethrough, overline, underline color, and blink. Because `textLength` is fixed to the total cell width, the string is aligned to terminal column boundaries rather than measured font width. Internal spaces are kept only when they connect to following characters with the same style, and trailing spaces at the end of a line are not output. This is the key to removing invisible data without changing positions on the screen.

Full-width characters occupy two columns, and the following continuation cell cannot be output as an independent character. Block elements and box-drawing characters are handled as shapes rather than fonts. Therefore, they become boundaries for string combining. By not naively “concatenating all adjacent characters,” console2svg prevents column drift and broken box lines.

## Shared optimization for still images and videos

Styles are not inlined each time; `SvgStyleRegistry` assigns short CSS classes to unique combinations. Shapes also use `SvgElementRegistry` in animation row definitions so that identical rectangles and paths can be defined and reused. This design reduces the raw SVG itself while avoiding repeated construction of the same strings during generation.

In video, row definitions are further deduplicated using visual row signatures. This uses the terminal property that, even in long recordings, often only a few rows change.

Performance improvement does not simply mean making the CPU faster; the priority is to create structures that do not need to be generated or drawn in the first place.
