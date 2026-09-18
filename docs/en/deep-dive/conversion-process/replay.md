---
title: Interpreting and playing replays
description: How operations are saved as key meanings and times, then resent as VT byte sequences rather than OS-dependent raw input.
---

Replays save reproducible input operations, not terminal output itself. This allows the same command to be run again in a PTY and screen changes to be re-recorded, including TUIs that respond to input. If OS-specific keyboard events were saved as-is, playback would not work across Windows and Unix, so files express common meanings: key name, modifiers, and time.

## Compact, hand-readable time representation

The first event has an absolute `time` in seconds from recording start, and subsequent events are saved as `tick` deltas from the previous event. Delta encoding avoids repeating the same large absolute values during continuous input and also makes JSON easier to read and write. During loading, `time` is prioritized, and events that only have `tick` are accumulated and normalized to absolute time. Version, application version, creation time, and total duration are also included, so boundaries of a recording that cannot be expressed by the event sequence alone can be represented.

Total duration is a boundary to prevent playback from becoming an indefinite recording when the child process remains waiting at a prompt even after input ends. The PTY path sets a limit of total duration plus one second, and the fallback path sets the same limit; when exceeded, it returns a timeout to the caller instead of ambiguously treating it as partial success.

## Converting keys to VT

`InputReplayFile.EventToBytes` converts Enter, Tab, arrows, Home/End, Insert/Delete, PageUp/Down, and F1 through F12 to standard VT sequences. Shift, Alt, Ctrl, and Meta are reflected in modifier parameters or ESC prefixes as needed. Ctrl+A through Ctrl+Z become control bytes, and normal characters and unknown key names fall back to UTF-8. `type: "raw"` sends `key` as UTF-8 as-is, allowing input that does not fit the normal model to be represented.

The playback stream prepares events as byte sequences in advance and waits until the scheduled time when read. If processing falls behind schedule, it does not wait extra and catches up without changing order. Even if a single event is larger than the stream read buffer, offsets are kept and the event is sent in chunks, so the middle of input is not lost. By connecting this stream to the same PTY writer as normal input forwarding, output acquisition uses the same path for manual input and replay.
