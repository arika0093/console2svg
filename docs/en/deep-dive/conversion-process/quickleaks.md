---
title: Automatic Masking with QuickLeaks
description: Precompiling secret detection rules into source code, anchor search, two-stage coordinate mapping, and physical value excision from SVG.
---

When sharing terminal session recordings, unintended exposure of API keys, passwords, and personally identifiable information is an ever-present risk.
To prevent credential leaks, console2svg incorporates an embedded secret detection engine named **QuickLeaks**.
Detection runs against terminal text immediately prior to SVG serialization, maps detected regions back to two-dimensional terminal cell coordinates, and physically excises secret strings from the SVG source code itself.

## Precompiled Rule Set and Architecture

QuickLeaks is pre-generated C# source code combining rule definitions from the **Betterleaks** secret scanner with console2svg-specific patterns.
With more than 400 detection rules embedded directly as native .NET code, QuickLeaks eliminates runtime external process spawns and external rule file loading.

However, QuickLeaks is intentionally scoped to identifying potential secret matches.
Online verification (communicating with external cloud services to test token validity) and heavyweight repository history traversal are deliberately omitted.
This keeps masking execution within a few milliseconds, avoiding capture latency.

## Acceleration via Anchor Search and Dedicated Verifiers

Evaluating hundreds of regular expressions from scratch across every frame incurs severe performance bottlenecks, particularly during animated SVG and video generation.
To eliminate this cost, QuickLeaks statically analyzes each regular expression and extracts invariant substrings (**anchors**) that must appear in any valid match.

At runtime, .NET's high-performance `SearchValues<string>` scans the entire screen text for all anchors in a single unified operation.
Only when an anchor is matched are the corresponding candidate rule bits activated.
Furthermore, for tokens with predictable structures—such as GitHub personal access tokens (`ghp_...`) or environment variable assignments—QuickLeaks runs **dedicated verifiers** that check character codes and string lengths directly without engaging the regular expression engine.

For rules that still require regular expressions, AOT-compatible `GeneratedRegex` and `RegexOptions.NonBacktracking` are applied with strict evaluation timeouts, preventing pathological inputs from stalling the conversion pipeline.

## Scoped Masking Preserving Syntactic Context

When the detection engine matches a pattern, it does not blindly redact the entire matched range.
Instead, it intentionally preserves contextual identifier structures so readers can still comprehend the command output.

For example, given `API_KEY=abcdef123456`, the key label `API_KEY=` remains intact, masking only the value `abcdef123456`.
In URIs containing credentials, the scheme (`https://`) and hostname are preserved while only the username and password fields are obscured.
Similarly, home directory paths retain their base structure, redacting only the user-specific directory name.

## Reconstructing Coordinates via Two-Stage Mapping

While QuickLeaks operates on flat plain text, the SVG rendering engine requires two-dimensional `(row, column)` grid coordinates.
To resolve this mapping efficiently, console2svg uses **two-stage mapping**.

In the first stage, a single normalized string is constructed by stripping trailing line padding and concatenating soft-wrapped rows, which is then passed to QuickLeaks.
If no secrets are found, the overhead of building a per-character coordinate lookup table is skipped entirely.

Only when secrets are detected does stage two re-scan the normalized string to construct a coordinate map recording the screen `(row, column)` for each character.
Because the vast majority of terminal screens contain no sensitive credentials, this avoids allocating per-character coordinate objects across benign captures.

## Physical Removal of Secret Strings from SVG Markup

Masked cells are never handled by simply placing an opaque rectangle over original text characters.
Because SVG is an inspectable, text-based vector format, merely overlaying a visual shape allows users to copy secret values via text selection or view them directly within the document source.

console2svg physically rewrites the underlying cell character data to replacement characters such as `*`, and then renders a visual striped pattern (mask overlay) in front.
Because replacement characters preserve exact cell width and column positions, downstream text alignment is never disrupted.

## Preventing Mask Interference in Animated Rendering

In animated SVGs, the "row delta" optimization overlays small in-line modifications on top of prior row definitions.
However, when manual mask patterns (`--mask`) are specified, row deltas are automatically disabled.

If a sensitive string happens to straddle the boundary between the unchanged prefix and the modified delta suffix, splitting the row across definitions would prevent pattern matchers from recognizing the complete secret.
Guaranteeing reliable credential concealment takes strict precedence over file size compression.
