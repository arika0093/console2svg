---
title: Automatic masking (QuickLeaks)
description: How detected results are mapped back to cell coordinates while preserving display layout, then replaced and overlaid in SVG.
---

As part of the conversion pipeline, QuickLeaks scans screen content before output. The important point here is not merely hiding strings, but preserving a readable SVG display without breaking terminal column width, wrapping, or full-width characters. Therefore, the detector and renderer are separated: the detector returns ranges in the string, and the renderer converts those ranges to cell coordinates.

## Why keep rules as generated code?

Betterleaks is used as the basis for the rules, but console2svg does not start an external scanner or read configuration files at runtime. Rules, keyword indexes, and regular expressions are imported into generated C# source.

Because normal mode and Early mode, which reacts more easily to values during input, can be generated separately, live display can cover values early while finalized output can reduce false positives.

`QuickLeaks.Enumerate` passes results in generation order without creating an array, and `Scan` returns them sorted by position only when necessary. Rendering uses the former and prioritizes screen generation over duplicating all detection results. Furthermore, instead of covering generic match ranges as-is, ranges are narrowed to leave prefixes useful for reading the screen, such as the value for `key=value` or the personal-name portion of a home directory.

## Mapping string matches back to cell drawing

When the renderer builds a target area into one string, it also records which `(row, column)` cell each UTF-16 character came from. Continuation cells for wide characters are treated as spaces, and line boundaries that are not ordinary wrapping insert newlines. This allows all corresponding cells to be collected correctly even when a string match spans lines.

Characters in the matching cells are replaced with `*`, and stripe rectangles that combine consecutive columns are placed on top. This avoids partial readability caused by text color or fonts while preserving column count, so subsequent character positions do not change. Manual `--mask` uses the same cell set and overlay. Manual patterns are not split at animation delta-row boundaries so that matches spanning change boundaries are not missed.
