---
title: Converting specific characters
description: How box-drawing and block elements, which easily break when left to font glyphs, are converted to SVG shapes based on cell geometry.
---

Terminal box-drawing characters and block elements are characters, but they also represent screen geometry. Even with monospaced fonts, glyph margins, thickness, and antialiasing vary by font, OS, and SVG renderer. If they are emitted as `<text>` as-is, gaps can appear between adjacent cells or TUI borders can look broken partway through. Therefore, only these characters are turned into SVG shapes derived from cell dimensions instead of being treated as text.

## Treat box-drawing as connection information

Characters such as `─`, `│`, corners, T junctions, crosses, heavy lines, and short segments are converted into information about which directions connect—left, right, up, and down—and into light/heavy line widths. Half-line segments from the center of each cell to the edges are collected once, and segments touching at the same position, color, and thickness are merged later. This makes it possible to output long borders as a few continuous paths rather than many small paths.

Merging is not only for reducing size. A single continuous path also reduces the problem where ends of adjacent strokes appear slightly separated when scaled down. Line width is proportional to font size, so the relationship between light and heavy is preserved even at sizes other than the default. Rounded box corners `╭╮╯╰` are drawn as quadratic Bézier curves sharing the same center point and are handled separately from orthogonal connections of normal box-drawing characters.

## Preserve area for block elements

Among Unicode Block Elements, characters representing fill amount compute rectangles from cell width and height. This prevents gaps caused by font baseline or glyph margins even when adjacent half blocks or quadrant blocks are arranged. Characters representing shades are intentionally left as text. They are characters that expect a font's halftone expression rather than a continuous surface.

These characters become boundaries when normal strings are combined so that the cell width occupied by shapes remains independent from `textLength` calculation. As a result, text, backgrounds, and shapes all live on the same cell coordinate system while each uses the most stable SVG primitive for its role.
