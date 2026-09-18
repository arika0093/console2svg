---
title: Interpreting sequences
description: Terminal emulation that reads split VT output with state and reflects it into cell screens, scrolling, and attributes.
---

Terminal output is not decorated text; it is a sequence of instructions that manipulate the screen. For example, progress displays return the cursor without adding newlines, and TUIs clear and redraw only specific areas. The conversion side executes instructions as a small virtual terminal, rather than handling strings with ANSI removed, in order to restore final cell placement.

## Chunk boundaries are not instruction boundaries

The OS read unit can split in the middle of `ESC [`, parameters, or the final byte. `AnsiParser` stores incomplete ESC sequences and incomplete OSC sequences displayed as `^[` by ECHOCTL, then prepends them to the next `Process` call. Without this state retention, the latter half of a split control instruction would be drawn as ordinary characters.

For CSI, private markers, parameters, and final bytes are separated. In the common case with few parameters, stack space is used; only when there are many parameters is an array from a shared pool used. This keeps GC from increasing even with large output and allows the parser to stay on the recording hot path. Unspecified values are resolved to each instruction's default, and unknown private CSI sequences are ignored rather than mistakenly interpreted as style instructions.

## Screen operations and text attributes

Implemented CSI includes cursor movement and absolute positioning, erase display/line, insert/delete characters, insert/delete lines, scrolling, scroll regions, tab stops, insert mode, and save/restore. DEC private mode handles alternate screen, origin mode, and cursor visibility. Full-screen applications use the alternate screen, so the main screen must be switched to a separate buffer without overwriting it and restored on exit.

SGR keeps bold, faint, italic, underline, blink, inverse, hidden, strikethrough, overline, 16 colors, 256 colors, RGB, and underline color in `TextStyle`. 256 colors are resolved as the first 16 theme colors, a 6×6×6 cube, and grayscale; truecolor is kept as RGB strings. Color arguments separated by `:` as well as `;` can be read, so differences in terminal SGR notation are reflected into the same color attributes.

Printable characters are placed while considering cell width. Surrogate pairs, variation selectors, and combining characters are combined into the previous cell, and zero-width characters do not advance the column. DEC special graphics G0/G1 character sets are also translated into box-drawing characters. The parser is not merely a layer that “turns byte sequences into strings”; it is a layer that restores meaning to the point where later stages can create coordinate-aware SVG.
