---
title: SVG format and style
description: Explain the structure and design of console2svg SVG output.
---

Generated SVGs are self-contained, resolution-independent images that can be embedded in Markdown or opened directly in a browser.

```text
<svg>
  <metadata> <!-- optional: version, embedded replay/cast/log -->
  <defs>     <!-- fonts, gradients, filters for chrome/shadow -->
  <g desktop-background> <!-- --background color/gradient/image -->
  <g window-chrome>      <!-- -d/--theme frame, title bar buttons -->
  <g terminal>           <!-- rows of <text>/<tspan> with ANSI colors -->
  <style>/<animate>      <!-- video timing when -v is used -->
</svg>
```

## Guarantees and expectations

- **Structure:** terminal text stays as selectable `<text>` with inline fill colors; layout uses a fixed monospace grid (`-w`/`-h`, `--padding`, `--margin`, `--pc-padding`).
- **Animation:** without `-v`, only the final frame is rendered. With `-v`, frames are embedded with SMIL timing (`--fps`, `--sleep`, `--timeout`).
- **Embedded metadata:** replay input, cast data, and verbose logs are opt-in only. They travel inside `<metadata>`; omit them for public docs. See [file formats](/reference/file-formats-and-embedded-metadata/).
- **Compatibility:** output targets evergreen browsers and common Markdown renderers. Custom web fonts are not embedded; keep `--font` to widely available monospace families when pixel-identical rendering matters.
- **Styling:** prefer `--theme` palettes over raw `--forecolor`/`--backcolor` so docs stay consistent; use `--opacity` only over desktop backgrounds (`*-pc` chrome).
