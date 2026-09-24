---
title: Interpreting Terminal Control Sequences
description: How stateful VT parsing processes fragmented input and accurately updates character cells, attributes, cursors, and screen buffers.
---

Terminal output is not merely a collection of styled text strings.
It is an instruction stream that continuously mutates screen state.
A carriage return (`\r`) returns the cursor to the beginning of a line to overwrite existing characters, escape sequences reposition the cursor without emitting printable text, and TUI applications switch to an alternate screen buffer to reconstruct the entire layout.
Simply stripping escape sequences with regular expressions discards the operational information required to reconstruct the final display.
console2svg interprets these instructions using a **stateful parser** (`AnsiParser`) and faithfully reflects them onto the virtual terminal buffer `ScreenBuffer`.

## Preserving Sequence State Across Stream Read Boundaries

Operating-system pipe read boundaries rarely align with escape sequence boundaries.
Data frequently arrives fragmented in the middle of an ESC or a **CSI** (Control Sequence Introducer: terminal control command starting with `ESC [`).

`AnsiParser` maintains an internal state machine.
When an incomplete sequence is encountered, it retains the partial state in an internal buffer and resumes parsing when subsequent bytes arrive in the next `Process` call.
Additionally, metadata streams that do not participate in visual layout—such as OSC (Operating System Command) and DCS (Device Control String)—are cleanly skipped until their terminating delimiters.
The parser also tracks state for DEC special graphics character sets, mapping them to appropriate Unicode box-drawing characters prior to rendering.

## Zero-Allocation Span-Based CSI Parsing

When extracting parameters (row indices, column indices, color codes) from CSI commands, console2svg avoids `string.Split` operations and intermediate string allocations entirely.
All numeric parsing operates directly over `ReadOnlySpan<char>` slices of the input buffer.

The parser counts the parameter count upfront; if there are 16 or fewer parameters, it stores values in a stack-allocated buffer (`stackalloc int[16]`).
Only in extreme cases with 17 or more parameters does it rent a buffer from `ArrayPool<int>`, returning it immediately after execution.
Because standard cursor movements and style modifications consist of only a few parameters, sequence parsing incurs zero heap allocation overhead.

## Screen Operations and Alternate Screen Reproduction

The implemented CSI command set includes relative and absolute cursor movement, screen and line erasures, character and line insertions and deletions, scroll margins, tab stops, and cursor state save/restore operations.

Furthermore, console2svg fully supports toggling to the **alternate screen** (the dedicated off-screen buffer utilized by tools such as vim or htop).
The alternate screen is maintained as an isolated buffer distinct from the primary screen that preserves shell history.
When a TUI tool exits and returns to the shell prompt, off-screen artifacts never contaminate the shell's scrollback history, accurately reproducing natural terminal behavior.

## Resolving Text Styles via SGR

Visual attributes and color designations are resolved via **SGR** (Select Graphic Rendition) commands.
A rich spectrum of attributes—including bold, faint, italic, underline, blink, reverse video, strikethrough, and double underline—is captured in `TextStyle` records.

Color support encompasses standard 16-color ANSI, the xterm 256-color palette, and 24-bit Truecolor (16.77 million colors).
Extended color syntaxes using both semicolon delimiters (`38;2;R;G;B`) and colon delimiters (`38:2::R:G:B`) are normalized into a unified internal color representation.
When consecutive cells share identical styles, existing `CellStyle` instances are reused directly, suppressing unnecessary object instantiation.

## Aligning Unicode Full-Width and Combining Characters to Cells

The number of UTF-16 code units manipulated by editors and programming languages does not correspond directly to terminal character cells (columns).
Surrogate pairs representing emoji are grouped into a single grapheme cluster prior to grid placement.
Zero-width characters (such as zero-width joiners) do not advance the cursor column, while combining characters and variation selectors are merged into the preceding cell.

East Asian full-width characters occupy two consecutive columns on screen.
console2svg places the character glyph in the leading cell and marks the adjacent right cell as a continuation cell.
Preserving this two-cell structure inside the emulator ensures that when the buffer is handed to the SVG renderer, subsequent text never drifts horizontally by one column.

## ScreenBuffer as a Unified Intermediate Representation

Once parsing is complete, downstream rendering pipelines (static SVG, animated SVG, PNG, video) never re-parse raw ANSI escape sequences.
Instead, they share **`ScreenBuffer`**—which integrates resolved text, color styles, cell widths, cursor states, and screen grids—as their sole intermediate representation.
Decoupling parsing responsibilities from rendering guarantees strictly consistent visual fidelity across all export formats.
