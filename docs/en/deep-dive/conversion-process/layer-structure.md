---
title: Organizing the SVG layers
description: How static chrome, terminal backgrounds, foreground content, cursors, and masks are separated while sharing one coordinate system.
---

A terminal frame contains parts that change at different rates.
Window chrome and a canvas background are usually static, cell backgrounds may change independently from glyphs, and the cursor can move without changing row text.
The SVG keeps those parts separate so animation does not duplicate content that can be reused.

## Resolve geometry once

`SvgDocumentBuilder.Context` computes the visible row and column range after cropping, then resolves cell metrics, margins, padding, chrome offsets, command-header height, canvas size, output size, and `viewBox`.

Cell width is derived as 0.6 times the configured font size.
Cell height is `18 / 14` times the font size, and the baseline offset is the font size.
Text, backgrounds, box drawing, block elements, cursors, and mask overlays all use those same values.

If only one output dimension is specified, the other is scaled proportionally.
If both are specified, the natural canvas is fitted inside the requested rectangle and the remaining area is represented through the view box.
The terminal grid itself is not given a different geometry for each output layer.

## Static outer layers

The SVG root defines output dimensions, the view box, common CSS, reusable definitions, and the canvas background.

Window chrome is placed above the canvas.
Desktop-style chrome can include its own background area, shadow, frame, and title region.
The terminal client region is then filled with the resolved terminal background so padding inside the chrome does not become unintended transparency.

A `--with-command` header belongs to the same static area.
Animated output writes these outer elements once rather than repeating them for each terminal state.

## Terminal background and foreground layers

A still image may render the terminal background and foreground in separate groups.
The background pass merges horizontal runs of the same effective background color and omits cells that match the base terminal background.

The foreground pass emits text runs, block geometry, box-drawing geometry, the cursor, and mask overlays.
Automatic and explicit mask scanning is skipped when a pass renders only backgrounds because no foreground text from that pass can expose a secret.

The separation also keeps masking above the text it replaces.
A mask does not depend on being hidden underneath another opaque terminal layer.

## Reusable styles and geometry

`SvgStyleRegistry` assigns one short CSS class to each unique effective text style.
The class records foreground color and decorations instead of repeating those attributes in every `<text>` node.

`SvgElementRegistry` performs a similar job for reusable rectangles and paths inside definitions.
The first geometry instance receives an ID; equivalent later instances use `<use>`.

The renderer omits zero-valued position attributes where the SVG default already gives the same result.
These small serialization choices matter because row definitions can repeat the same structural attributes many times.

## Animation layer boundary

Animated output places unique row content in `<defs>`.
The visible terminal body contains `<use>` elements that select row definitions over time with discrete SMIL display intervals.

Cursor runs are emitted separately from the row definitions.
A cursor-only change therefore affects only the cursor layer.

The background, chrome, command header, coordinate transform, and crop remain outside the row-switching mechanism.
As a result, still SVG and animated SVG use the same terminal geometry and color interpretation; animation changes how repeated content is referenced, not how a cell is laid out.
