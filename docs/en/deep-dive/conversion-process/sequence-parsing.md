---
title: Interpreting terminal control sequences
description: How incremental VT parsing updates cursor state, cell contents, attributes, scrolling, and alternate screens.
---

Terminal output is a stream of drawing operations.
A carriage return can overwrite an existing line, CSI can move the cursor without printing text, and a full-screen application can switch to an alternate screen and redraw only selected regions.
Removing escape sequences would discard the operations needed to reconstruct that display.

console2svg therefore parses output into a stateful `ScreenBuffer` before SVG generation.

## Preserve parser state across reads

An operating-system read can end in the middle of ESC, CSI, OSC, or another control sequence.
`AnsiParser` retains incomplete sequence text and resumes it on the next `Process` call.

Unix echo handling can also expose a control sequence in caret notation, such as `^[` for ESC.
The parser keeps a separate pending path for the OSC form that can arrive through that representation.
Ordinary `^[` text is not treated broadly as an escape sequence; the special handling is constrained so visible text is not consumed accidentally.

OSC and DCS payloads are skipped until their terminator because they are control strings rather than printable terminal cells.
G0 and G1 character-set designation and SO/SI selection are retained as parser state.
DEC special graphics can therefore be mapped to the corresponding box-drawing characters before rendering.

## Parse CSI without string splitting

CSI parameters are read from spans instead of being tokenized with `string.Split`.
The parser counts the parameters first.
Up to 16 integer parameters use a stack-allocated span; larger sequences rent an integer array from `ArrayPool<int>` and return it afterward.

This keeps the common SGR and cursor-control path free from one array allocation per sequence.
Private markers are parsed separately from the parameter list, and unsupported private sequences are ignored instead of being interpreted as unrelated standard commands.

## Apply screen operations

Implemented CSI operations include relative and absolute cursor movement, erase display and erase line, insert and delete characters, insert and delete lines, scrolling, scroll regions, tab control, insert mode, repeat, and save or restore operations.

DEC private modes cover the alternate screen, origin mode, and cursor visibility.
The alternate screen is a separate cell buffer.
Leaving a TUI can therefore restore the main screen rather than leaving the full-screen application's cells mixed into shell history.

Cursor save and restore includes the terminal state that affects subsequent placement.
The emulator must reproduce the future effect of a control sequence, not only the cells visible at the instant that sequence is parsed.

## Resolve text attributes into cell style

SGR updates a `TextStyle` that includes bold, faint, italic, underline, blink, inverse, hidden, strikethrough, overline, foreground, background, and underline color.

The parser accepts the conventional 16-color ranges, the xterm 256-color palette, and true RGB color.
The 256-color palette resolves the first 16 entries through the active theme, followed by the 6 by 6 by 6 color cube and grayscale range.
Extended color forms separated with either semicolons or colons are normalized into the same style state.

`ScreenBuffer` interns equivalent styles so adjacent cells do not each need their own style object.
A last-style fast path handles the common case where many characters are printed under the same SGR state.

## Keep Unicode aligned to terminal cells

A UTF-16 code unit is not necessarily one terminal cell.
Surrogate pairs are combined before placement.
Combining marks and variation selectors are appended to the preceding cell, and zero-width characters do not advance the cursor.

Wide characters occupy two columns.
The leading cell stores the text and the following cell is marked as a continuation.
Operations that overwrite or take row deltas account for those continuation cells so later SVG text runs cannot drift by one column.

Variation selector 16 can turn a previously narrow symbol into a wide emoji presentation when the grid has space for it.
That adjustment happens in the cell model, before any SVG geometry is chosen.

## Use the screen buffer as the conversion boundary

After parsing, later renderers do not need to reason about raw CSI syntax.
They receive rows of cells with resolved text, style, width flags, cursor position, active screen, and scroll state.

The same buffer also carries visual signatures and copy-on-write row sharing used by animation and video sampling.
That makes terminal interpretation a single semantic boundary: parsing happens once, while SVG, PNG, and video paths consume the resulting screen state.
