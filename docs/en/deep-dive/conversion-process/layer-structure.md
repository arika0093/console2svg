---
title: SVG Layer Structure
description: How window chrome, terminal backgrounds, text foregrounds, cursors, and masks are separated and integrated under a shared grid coordinate system.
---

Elements composing a terminal display do not change at the same rate.
Window frames and desktop backgrounds remain completely static, terminal character cells update row by row, and the cursor moves without modifying underlying text content.
console2svg leverages these differences in update frequency for **layer separation**, eliminating redundant drawing instructions across both static images and animations.

## Architectural Overview of SVG Layers

The SVG structure assembled by console2svg follows a hierarchical group (`<g>`) organization:

```xml title="Static SVG element structure example"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 898 320" width="898" height="320">
  <!-- Shared definitions: stylesheets and reusable geometry -->
  <defs>
    <style>
      .c2s-bold { font-weight: bold; }
      .c2s-fg-red { fill: #ff5555; }
      .c2s-fg-cyan { fill: #8be9fd; }
    </style>
  </defs>

  <!-- Layer 1: Desktop canvas background -->
  <rect class="c2s-canvas-bg" width="100%" height="100%" fill="#1e1e2e"/>

  <!-- Layer 2: Window chrome (title bar, close/minimize buttons, shadow) -->
  <g class="c2s-window-chrome" transform="translate(20, 20)">
    <rect class="c2s-window-frame" width="858" height="280" rx="8" fill="#181825"/>
    <circle cx="16" cy="16" r="6" fill="#ff5f56"/>
    <circle cx="36" cy="16" r="6" fill="#ffbd2e"/>
    <circle cx="56" cy="16" r="6" fill="#27c93f"/>

    <!-- Layer 3: Base background for terminal client area -->
    <rect class="c2s-terminal-bg" x="0" y="32" width="858" height="248" fill="#11111b"/>

    <!-- Layer 4: Command header (when --with-command is specified) -->
    <g class="c2s-command-header" transform="translate(14, 52)">
      <text class="c2s-prompt" fill="#a6adc8">$</text>
      <text class="c2s-command" x="16" fill="#cdd6f4">fastfetch</text>
    </g>

    <!-- Layer 5: Terminal body (grid coordinate system) -->
    <g class="c2s-terminal-body" transform="translate(14, 76)">
      <!-- 5a. Background pass: merged rectangles for cells with non-default background colors -->
      <g class="c2s-bg-pass">
        <rect x="0" y="0" width="120" height="18" fill="#313244"/>
      </g>

      <!-- 5b. Foreground pass: merged text strings, box-drawing paths, block elements -->
      <g class="c2s-fg-pass" font-family="JetBrains Mono, monospace" font-size="14">
        <text y="14" class="c2s-fg-cyan c2s-bold">OS:</text>
        <text x="32" y="14" fill="#cdd6f4">Ubuntu 24.04 LTS</text>
        <!-- Consolidated box-drawing path -->
        <path d="M 0 36 L 240 36" stroke="#45475a" stroke-width="1"/>
      </g>

      <!-- 5c. Cursor layer (independent from text body) -->
      <rect class="c2s-cursor" x="168" y="0" width="8.4" height="18" fill="#f5e0dc" opacity="0.8"/>

      <!-- 5d. Mask overlay (striped pattern over replaced characters) -->
      <rect class="c2s-mask" x="84" y="18" width="84" height="18" fill="url(#mask-stripe)"/>
    </g>
  </g>
</svg>
```

In this manner, unchanging static window frames and canvas backgrounds remain pinned on outer layers, while the inner grid group focuses exclusively on terminal cell content.

## Determining Grid Coordinates with a Unified Context

Layout across the entire canvas is centrally computed by `SvgDocumentBuilder.Context`.
Taking the visible row and column counts after cropping as the baseline, it calculates cell dimensions, outer margins, inner padding, window chrome offsets, command header height, and the SVG **`viewBox`** (a resolution-independent virtual coordinate space).

Cell width is derived as 0.6 times font size, cell height is calculated as `18 / 14` times font size (approximately 1.286x), and character baseline placement is strictly aligned with font size.
Background fill rectangles, text glyphs, box-drawing and block graphics, cursors, and secret mask strips all reference the grid coordinates established by this shared context.
Because individual layers do not manage independent margin calculations or coordinate transformations, no gaps or misalignments occur between text and decorative elements.

When only one output dimension (width or height) is specified, the other dimension is computed proportionally to preserve aspect ratio.
When both dimensions are explicitly provided, the terminal display is fitted inside that rectangle, and extra margins are absorbed through `viewBox` mapping.

## Emitting Outer Static Layers Once

The outermost shell of the SVG document defines total dimensions, the `viewBox`, shared CSS style rules, and the desktop canvas background.

Nested within is the macOS-styled window frame (chrome border, drop shadow, and title bar).
Next, the entire terminal display area is filled with the resolved base terminal background color, preventing internal padding from becoming unintentionally transparent against the canvas background.

The `--with-command` header that displays the executed command also resides within this static layer.
Even when generating animated SVGs, these static elements are emitted only once directly under the document root rather than duplicated for each frame.

## Separating Background and Foreground Rendering

During static rendering, terminal cell background colors and text/graphic foregrounds are drawn in separate passes.

The background pass merges horizontally contiguous cells sharing the same background color into a single large rectangle (`<rect>`).
Cells matching the terminal's default background color emit no rectangles, relying directly on the parent layer's background fill.

The foreground pass outputs text runs (`<text>`), box-drawing paths, block elements, cursors, and mask overlays.
Mask overlays that conceal sensitive data are positioned over already-sanitized replacement characters, as detailed later.
console2svg never simply layers an opaque rectangle over original secrets while leaving raw secret text in the underlying markup, preventing passwords from being copied directly out of the SVG source code.

## Sharing Styles and Geometry

Styles and shapes that appear repeatedly throughout the document are centralized in the SVG definitions section.

For text styles, `SvgStyleRegistry` assigns a concise CSS class name to each unique combination of color and decoration (such as bold or underline).
Omitting repetitive inline styling attributes from individual `<text>` elements dramatically reduces markup bloat.

Similarly, for reusable icons and boilerplate graphics, `SvgElementRegistry` attaches an identifier to the first geometry instance and substitutes `<use>` elements at subsequent occurrences.
Coordinate attributes with a value of zero that match SVG defaults are omitted entirely, minimizing DOM byte size.

## Localizing Dynamic Updates in Animations

In animated SVGs, unique row definitions reside within `<defs>`, referenced from the visible terminal body via `<use>` elements.
Display transitions are driven entirely through discrete SMIL visibility intervals.

The cursor layer is emitted independently from this row catalog structure.
When the cursor moves during typing, row definitions and backgrounds do not need to be regenerated.

Outer window chrome, command headers, and background imagery persist outside the row-switching loop.
While maintaining uniform coordinate systems and color interpretations across static and animated outputs, only rows and cursors requiring dynamic updates are switched.
