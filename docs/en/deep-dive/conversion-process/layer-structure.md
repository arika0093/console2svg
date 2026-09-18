---
title: Layer structure
description: An SVG hierarchy that separates appearance settings from terminal content and reuses static parts during animation.
---

The output SVG does not mix “terminal content” and “the frame it is placed in.” Even if only the screen content changes, there is no need to redraw the background image, macOS-style or Windows-style window decoration, margins, or command header. Especially in animation, the static outer frame is output once, and only terminal rows are switched inside it.

## Order from canvas to terminal

First, the SVG `width`, `height`, `viewBox`, common font CSS, and style definitions are placed. Next, the canvas background is drawn.

For chrome with a desktop, the default gradient or specified background is placed over the whole screen; for normal chrome, only the explicitly specified background is placed. Local images can be embedded as data URIs, so even when distributed as a single file, the background does not depend on relative paths.

The window shadow, frame, and title bar are placed on top, and then the terminal client area is filled with the theme background. This prevents the padding area from being transparent and makes the inside of the chrome appear as a single terminal.

The `--with-command` header is also included in this static area. Finally, terminal frames are placed, layered in the order of cell backgrounds, text, shape fragments, cursor, and mask overlays. Putting backgrounds first ensures that inverse-color cells and translucent cursors always have the correct base.

## Decide coordinates in one place

`SvgDocumentBuilder.Context` resolves cropping, scrollback, margin, padding, chrome, headers, and `--size`, then keeps the body start coordinates and display range. Cell width is 0.6 times the font size, cell height is `18 / 14` times, and the baseline is based on the font size. Because every terminal element refers to this same context, coordinate calculations for background rectangles, text, box-drawing lines, and cursors do not drift separately.

Even when output size is specified, the body geometry is not directly stretched. If only one side is specified, SVG dimensions are chosen while preserving the ratio; if both are specified, a fitting scale is selected and the excess becomes the background area on the `viewBox` side. This separation allows output to match the outer dimensions required by the embedding destination while keeping the natural layout including chrome.

## Boundaries in animation

In animated SVGs, the outer background, chrome, and header are closed only once, and body row definitions are output to `<defs>`. Then a group for content offsets is opened, and each row's `<use>` and SMIL are placed. Because layer order is shared with still images, the meaning of colors and cropping does not change before and after conversion to video.
