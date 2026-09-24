---
title: Vectorizing Box-Drawing and Block Elements
description: Converting box-drawing characters and block elements into cell-aligned vector geometry to eliminate font-dependent gaps and misalignments.
---

Terminal TUI applications utilize diverse Unicode symbols to construct window frames, dividers, and progress bars.
However, when these symbols are rendered as standard font glyphs within `<text>` elements, unnatural seams, hairline gaps, and uneven stroke weights frequently emerge along adjacent cell boundaries.
Even among monospaced fonts, different rendering environments (browsers, operating systems, antialiasing filters) interpret glyph bounding boxes and baseline offsets with subtle variations.
To eliminate these rendering defects, console2svg transforms **box-drawing characters** and **block elements** away from text glyphs into pure vector geometry (`<path>` and `<rect>`) computed directly from the terminal cell grid.

## Decomposing and Merging Box-Drawing Characters into Line Geometry

Box-drawing characters (`─`, `│`, `┌`, `┐`, etc.) are decomposed into directional connectivity vectors extending from the cell center toward the top, bottom, left, and right edges.
Line thickness variations (light, heavy), double lines, and dashed segments are mapped to corresponding stroke widths and coordinate offsets.

Rather than emitting an isolated SVG path for each cell as it is visited, the renderer first collects all horizontal and vertical line segments across the entire screen.
It sorts segments sharing identical coordinate axes, colors, and stroke widths, and merges continuous segments whose endpoints touch or overlap into unified, extended lines.

The proximity tolerance for segment merging is evaluated relative to cell dimensions rather than as an absolute pixel value.
This prevents genuinely disconnected segments from being erroneously merged when unusually small font sizes place absolute coordinates very close together.
Merged segments are treated as rectangular path geometries, and all paths sharing the same color are consolidated into a single `<path d="...">` attribute, simultaneously minimizing DOM element counts and accelerating rendering performance.

## Rendering Rounded Corners with Quadratic Bézier Curves

Rounded corner characters (`╭`, `╮`, `╯`, `╰`) are handled distinctly from orthogonal line intersections.
Rather than forcing them into the orthogonal line-segment model, console2svg constructs independent arc paths using **quadratic Bézier curves** (smooth curves defined by three mathematical control points) anchored to cell grid coordinates.
This reproduces smooth, aesthetically pleasing rounded corners characteristic of modern TUI designs with complete geometric fidelity.

## Converting Block Elements into Grid Rectangles

**Block elements** that fill portions of a cell—such as full blocks `█`, half blocks `▀` `▄` `▌` `▐`, and quadrant blocks—are calculated as precise geometric rectangles derived from cell width and height.

By completely bypassing font glyph side bearings and metrics, the renderer produces `<rect>` elements that snap strictly to terminal grid boundaries, guaranteeing zero visible gaps between adjacent blocks.
Contiguous block rectangles sharing identical fill colors are coalesced into a single larger rectangle prior to serialization.

In contrast, shaded block characters (`░`, `▒`, `▓`) are intentionally preserved as normal text glyphs.
These represent textured stipple density patterns characteristic of specific fonts rather than solid geometric fills.

## Isolating Geometric Elements from Text Runs

When serializing character strings, encountering a box-drawing or block element immediately terminates the currently active `<text>` element.

In SVG, the `textLength` attribute is used to enforce precise monospaced glyph spacing across varied font environments.
If geometric shapes were embedded directly within text runs, `textLength` letter-spacing adjustments would distort the cell coordinates of the vector geometry.
Terminating the text run before each geometric shape and opening a fresh `<text>` element immediately afterward guarantees subpixel coordinate alignment between text and vector shapes across complex TUI interfaces.
