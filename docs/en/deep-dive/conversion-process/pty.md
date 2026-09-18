---
title: Starting a PTY and recording its output
description: How console2svg creates a terminal-like child process, forwards input, batches output, and handles shutdown.
---

Redirected standard output does not behave like a terminal.
Programs can change color support, buffering, progress rendering, and full-screen UI behavior after detecting whether they are connected to a TTY.
console2svg therefore records commands through a pseudoterminal when possible.

## Platform PTY boundary

The current recording layer uses `Porta.Pty` to create and manage the platform PTY.
console2svg configures the process, streams, dimensions, environment, forwarding, timing, and shutdown around that backend rather than implementing each operating-system PTY API inside the repository.

On Windows, the terminal-facing stream follows pseudoconsole semantics: terminal text and virtual-terminal control sequences travel through byte streams.
On Unix-like systems, the corresponding model is the conventional PTY controller and terminal pair.

The child receives the requested column and row counts, and console2svg also sets `COLUMNS` and `LINES`.
Commands run through `cmd.exe` on Windows and `/bin/sh` on Unix-like systems so shell syntax accepted by the CLI remains available.
Windows command-line arguments are pre-quoted because process creation ultimately consumes a single command-line representation; the `cmd.exe /c` payload has separate quoting rules from ordinary C-runtime arguments.

By default, console2svg removes selected CI environment markers such as `CI` and `TF_BUILD` before invoking the shell.
Some terminal libraries disable color or interactive formatting when those variables are present.
The removal can be disabled when preserving the parent environment is more important.

## Reading output without losing encoding state

The PTY output reader keeps one byte buffer, one character buffer, and one stateful decoder for the complete read loop.
A multi-byte UTF-8 character can therefore be split across two operating-system reads without being decoded as two invalid fragments.

When output is mirrored to a byte stream, the original bytes are forwarded directly.
They are not decoded and re-encoded first, so VT sequences are not modified by the forwarding path.
On Windows, the text-output forwarding path temporarily selects UTF-8 where appropriate.

Each captured text batch is paired with the elapsed recording time and appended to `RecordingSession`.
The output is not split by line because carriage returns, cursor movement, erases, and alternate-screen operations are meaningful to the later terminal emulator.

## Coalescing small writes

One visual update can arrive through many small PTY reads.
Storing every read as a separate recording event would make the ANSI parser and animation reducer process boundaries that do not necessarily correspond to visible states.

The default recorder groups nearby output chunks.
Its coalescing window is one quarter of the target video-frame interval, clamped to 2 through 20 milliseconds.
A batch is also limited to one frame interval so a continuous output stream cannot postpone event emission indefinitely.

An explicit coalescing option can override that behavior or disable it.
The timestamp assigned to a coalesced event is the time of the last chunk in the batch.

## Forwarding interactive input

Interactive capture places the host input in a raw form so key sequences can be forwarded to the child rather than interpreted locally.
On Unix-like systems, console2svg prefers `/dev/tty` when standard input is redirected but an interactive terminal is still available.

VT input is decoded as UTF-8 when it is recorded for replay.
This is independent of the legacy console code page.
Escape sequences are ASCII, and treating ESC through a stateful non-UTF-8 code page can consume or reinterpret bytes that belong to arrow keys and other CSI sequences.

Incomplete input escape sequences are carried into the next read when replay recording is enabled.
The recorder does not turn a truncated CSI prefix into an unrelated key event merely because a stream read ended there.

## Suppressing echoed control input

Live host input is written into the PTY.
If the PTY slave echoes that input, control bytes can reappear in captured output.
On Unix-like systems, `ECHOCTL` may render a control character such as ESC using caret notation.

For live forwarding, console2svg therefore attempts to disable the PTY slave echo flags through the controller stream.
The operation is best effort because support depends on the platform and backend.
Replay input does not need the same host-input echo handling.

The host terminal is also restored after capture.
Mouse-tracking modes used by full-screen applications are disabled on exit so they do not remain active in the user's terminal session.

## Process exit and remaining output

A child process can exit before all bytes already written to the PTY have been read by the parent.
console2svg therefore gives the output reader up to 500 milliseconds to drain after process exit.

A closed PTY may not look identical on every platform.
An I/O error produced by PTY teardown is treated as end-of-stream when it matches the expected PTY-close case, so buffered recording data can still be finalized.

Cleanup is bounded as well.
Connection disposal and output-reader shutdown each have a one-second upper bound.
A stuck backend should not leave the CLI waiting indefinitely during teardown.

## Startup retry and fallback

PTY creation can fail because a native backend is unavailable, incompatible with the host, or starts without producing usable output.
The recorder retries a startup hang up to three times, with a short delay between attempts.

If those attempts fail, or the PTY backend cannot be loaded, console2svg falls back to a process with redirected streams.
The fallback cannot reproduce every TTY-dependent behavior, but it allows non-interactive commands to remain usable instead of turning a missing PTY implementation into a permanent hang.
