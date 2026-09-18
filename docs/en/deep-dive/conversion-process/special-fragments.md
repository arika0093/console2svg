---
title: Rendering box drawing and block elements
description: How terminal glyphs that represent geometry are converted into cell-aligned SVG shapes and merged into compact paths.
---

Some Unicode characters represent terminal geometry more directly than ordinary text.
Box-drawing characters form connected borders, while block elements represent fixed fractions of a cell.

Rendering those characters only through a font can introduce visible gaps.
Glyph bearings, stroke thickness, hinting, and antialiasing vary across fonts and SVG rasterizers even when the font is monospaced.
console2svg therefore converts the supported geometric characters into SVG shapes based on the terminal cell dimensions.

## Convert box characters into segments

A box-drawing character is interpreted as connections from the cell center toward the left, right, top, or bottom edge.
Light and heavy variants select different stroke widths.
Short line variants produce only the corresponding partial segment.

The renderer collects horizontal and vertical half-segments instead of writing one SVG path immediately for each character.
Segments with the same axis position, color, and stroke width are sorted and merged when they touch or overlap.

The merge tolerance is relative to the cell dimensions.
Using a cell-relative tolerance avoids joining genuinely separate segments when an unusually small font size makes absolute coordinates very close together.

Merged line rectangles are then grouped by color and emitted as compact path data.
This reduces the number of SVG elements and also removes small joins that could become visible between adjacent per-cell strokes after scaling.

Rounded corners such as `╭`, `╮`, `╯`, and `╰` are handled separately with quadratic Bézier curves.
They use the same cell coordinate system but are not reduced to the orthogonal segment model.

## Convert block elements into rectangles

Block elements that express filled portions of a cell are mapped to rectangles computed from cell width and height.

Upper and lower fractions, left and right fractions, and quadrant combinations therefore align to exact cell boundaries instead of depending on the selected font's glyph box.

Compatible adjacent block rectangles are merged before serialization when they form one continuous filled area with the same color.
This reduces repeated geometry without changing the occupied terminal cells.

Shade characters remain text.
Their visual meaning depends on a pattern of glyph dots rather than on a continuous filled region, so replacing them with a solid rectangle would change the character.

## Keep geometry outside text runs

A geometry-rendered character terminates the current foreground text run.

This boundary prevents `textLength` from being asked to account for a cell whose visible content is actually drawn as an independent shape.
The following text run starts from its terminal column, so text, block geometry, and box paths stay aligned to the same grid.

The same geometric conversion is used by still SVG, animated row definitions, and the SVG frames rasterized for video.
Differences between those output modes therefore do not change how terminal borders are constructed.
