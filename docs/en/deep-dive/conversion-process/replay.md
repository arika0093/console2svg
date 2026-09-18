---
title: Recording and playing input replays
description: How interactive input is normalized into timed key events and later converted back into VT byte sequences for the PTY.
---

A replay records input operations, not terminal output.
Playback starts the command again in a PTY and sends the recorded operations at their scheduled times, allowing the resulting terminal output to be captured again.

The file therefore stores cross-platform key meaning rather than native Windows console records or Unix input structures.

## Normalize timing in the replay file

The first input event can carry an absolute `time` from recording start.
Later events can use `tick` values relative to the previous event.

On load, absolute time takes precedence where present and relative ticks are accumulated into a normalized timeline.
The replay metadata also records format and application information together with total duration.

Total duration is not inferred only from the last key.
A child can remain at a prompt after all input has been delivered.
Playback uses the stored duration as a recording boundary and allows one additional second before reporting a timeout.

## Decode live input without breaking VT sequences

When interactive input is also being saved as a replay, the forwarding path still sends the original bytes to the PTY first.

A UTF-8 decoder is used to interpret those bytes for the replay model.
VT key sequences are ASCII, and using a legacy console code page to interpret ESC can consume or reinterpret sequence bytes on some Windows configurations.

A stream read can stop in the middle of CSI, SS3, OSC, DCS, APC, PM, or SOS traffic.
The replay parser detects an incomplete trailing escape sequence and carries it into the next input chunk instead of converting the partial prefix into an unrelated key.

Terminal protocol control strings are excluded from user-key replay events.
They can be responses or terminal traffic rather than a key the user intended to reproduce.

## Store key meaning instead of raw host events

Common keys are normalized to names such as `ArrowUp`, `Home`, `Delete`, and `F1` through `F12`.
Modifiers are represented independently.

Printable Unicode text is stored as text rather than as a platform key code.
Surrogate pairs are preserved as one logical key value.

A `raw` replay event remains available for byte-oriented input that does not fit the normal key model.

## Convert replay events back to VT bytes

During playback, key names and modifiers are converted into the VT sequences expected by a terminal application.

Enter, Tab, arrows, navigation keys, and function keys use their corresponding escape sequences.
Ctrl plus a letter maps to its control byte.
Alt can be represented with an ESC prefix, while printable text is encoded as UTF-8.

The same PTY writer used for live input receives these replay bytes.
Output capture therefore follows the same terminal path whether input came from the keyboard or from a replay file.

## Schedule bytes without reordering them

The replay stream prepares the byte representation of events and waits until each event's target time when the stream is read.

If processing is already behind the scheduled time, the stream does not add another artificial delay.
It continues in original order and catches up.

An event can be larger than the consumer's read buffer.
The stream keeps an offset into the current event and returns it over multiple reads, so splitting at the stream boundary does not drop the remainder of an input operation.
