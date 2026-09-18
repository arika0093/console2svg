---
title: Automatic masking with QuickLeaks
description: How console2svg narrows Betterleaks-derived matches, maps them back to terminal cells, and avoids scanning work on the common no-match path.
---

Automatic masking operates on terminal text before that text is serialized into SVG.
The detector reports UTF-16 ranges, while the renderer decides which terminal cells those ranges cover.
Keeping those responsibilities separate lets detection work on ordinary strings without losing the cell geometry required for rendering.

## QuickLeaks is a local detector, not Betterleaks itself

`ConsoleToSvg.QuickLeaks` is generated from a pinned Betterleaks rule set plus console2svg-specific rules.
The current generated source contains 465 rules.

QuickLeaks retains Betterleaks rule IDs, keywords, and regular expressions, but it does not embed the complete Betterleaks execution model.
Expression filters, validators, provider or network checks, and repository-context logic are intentionally omitted.
A QuickLeaks finding therefore means that text matched a secret-like pattern; it does not confirm that the value is a valid or live credential.

Keeping the detector as generated C# also removes a runtime dependency on a separate scanner executable or rule-configuration file.

## Filter candidate rules before running regexes

Running hundreds of regular expressions over every rendered screen would make automatic masking expensive, especially for animation.

The generator builds an Aho-Corasick automaton over lowercased ASCII rule keywords.
A screen is scanned once through that automaton, and matching keywords set bits in a compact candidate-rule bitset.
Only candidate rules then execute their source-generated regular expression.

The automaton tables are packed into constant UTF-16 strings instead of large generated array initializers.
Transition lookup uses a direct case for a single outgoing edge, a short linear scan for small fan-out, and binary search for larger fan-out.
The generated regexes use `GeneratedRegex` with a fixed match timeout so one pathological input cannot leave a rule evaluating without a bound.

Rules with no applicable keyword still need the behavior encoded by their generated candidate mapping.
The keyword stage is an acceleration mechanism, not a replacement for the rule regex.

## Detect partially entered values in Early mode

`QuickLeaksScanMode.Early` exists for content that may be rendered while a value is still being typed.

The generator relaxes fixed-length quantifiers only inside the rule's secret-value capture.
For example, a captured token that normally requires 32 characters can match a shorter prefix while it is being entered.
Contextual quantifiers outside the secret capture are left unchanged.

Early mode increases the chance of false positives and is therefore a separate mode rather than the default final-output behavior.

## Preserve readable context around a match

The raw regular-expression match is not always the range that should disappear from the SVG.

For generic `key=value` forms, QuickLeaks narrows the finding to the value and leaves the key visible.
For credential URIs, console2svg can mask the username and password independently while preserving the scheme, separators, and host.
The home-directory rule leaves the directory prefix and narrows the sensitive range to the user-specific component.
The Git identity rule masks the display name and the local part of the email separately while preserving the email domain.

These transformations preserve enough structure to understand what was printed without retaining the detected value itself.

## Map detected characters back to cells

The renderer first normalizes the visible terminal region into a string.
Wide-character continuation cells are represented so column mapping remains consistent, trailing blank cells are removed, and a newline is omitted when one physical row is the continuation of a wrapped terminal line.
A token can therefore remain contiguous in the detector input even when the terminal wrapped it across rows.

Automatic masking uses two passes on the common rendering path.
The first pass builds only normalized text and runs QuickLeaks.
If there are no findings, no per-character coordinate list is allocated.

Only after a finding exists does the renderer rebuild the normalized text while recording the originating `(row, column)` for each character.
The finding ranges can then be converted into a set of terminal cells.

## Remove the secret from SVG text

Matched cells are not left intact underneath an opaque rectangle.
Their rendered text is replaced with `*`, and consecutive masked cells also receive the striped mask overlay.

Replacing the text matters because SVG remains inspectable source.
An overlay alone would hide the value visually while leaving the original string available to copy, search, or inspect in the document.

The replacement keeps the terminal cell count unchanged, so later text does not shift horizontally.

## Keep masking compatible with animation reuse

Automatic and explicit masking run only when foreground content is being rendered.
A background-only layer does not allocate detection state.

The reusable frame-render workspace supplies the normalized-text `StringBuilder` so repeated row rendering can reuse its backing storage.

Manual mask patterns also constrain row-delta compression.
A literal pattern can span an unchanged base portion and a changed delta portion.
When explicit patterns are present, console2svg keeps the complete row context instead of splitting it into a delta that could hide that cross-boundary match.
