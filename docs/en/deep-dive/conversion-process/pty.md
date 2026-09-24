---
title: PTY Launch, Control, and Output Capture
description: How child processes are executed via pseudoterminals, managing I/O forwarding, control sequence preservation, and shutdown.
---

Merely redirecting standard output to a conventional pipe does not reproduce the behavior of interactive terminal commands.
Many command-line utilities detect whether standard output is connected to a terminal (TTY) and automatically disable color output, progress bars, and interactive UI elements when a pipe is detected.
console2svg circumvents this limitation by executing child processes through a **PTY** (pseudoterminal: an operating system mechanism providing terminal emulation), recreating an environment connected to an authentic terminal.

## Platform PTY Backend Abstraction

PTY creation and lifecycle management are handled using the `Porta.Pty` library.
Rather than invoking platform-specific system calls directly, console2svg encapsulates the differences between Windows ConPTY (Pseudoconsole API) and Unix PTYs (controller/terminal pairs) at the library boundary, exposing them as a unified stream to upper layers.

When spawning a child process, the configured terminal width (columns) and height (rows) are applied, and identical values are injected into the `COLUMNS` and `LINES` environment variables.
Shell commands are invoked through `cmd.exe /c` on Windows and `/bin/sh -c` on Unix-like systems.
Because Windows `cmd.exe` command-line parsing differs from standard C-runtime quoting rules, console2svg applies specialized escaping to assemble safe command strings on Windows.

By default, CI-related environment variables such as `CI` and `TF_BUILD` inherited from the parent process are automatically scrubbed from the child environment.
Modern development tools and testing frameworks often force non-interactive, monochrome output whenever these variables exist, even when running within a valid PTY.
Users can preserve these variables by specifying the `--no-delete-envs` option.

## Reading Streams Without Splitting Character Boundaries

The PTY output reading loop maintains a single byte buffer, a single character buffer, and a stateful UTF-8 decoder across its entire lifecycle.
Even when multi-byte UTF-8 sequences (such as full-width characters) are fragmented across operating-system read boundaries, incomplete byte states are carried forward into the next read operation without corruption.

When streaming output in real time to the host terminal, raw bytes are forwarded directly to standard output.
Bypassing intermediate decoding and re-encoding avoids processing overhead and prevents the structural corruption of **VT sequences** (escape sequences governing cursor movement and text styling).

Captured text strings are appended to `RecordingSession` alongside their elapsed timestamps.
Output is deliberately not split by line breaks at this stage, because carriage returns (`\r`), cursor repositioning, line erasures, and screen buffer toggles represent essential state transitions required by the downstream terminal emulator.

## Coalescing Proximate Output Events

Even during a single screen redraw, child process output frequently arrives fragmented across numerous small PTY reads.
Generating a separate recording event for every read would force the downstream ANSI parser to interpret meaningless intermediate states, incurring substantial processing overhead.

To prevent this, console2svg applies **output coalescing** to group temporally proximate writes into a single event.
The aggregation window is set to one quarter of the video frame interval, clamped between 2 and 20 milliseconds.
The coalesced event is timestamped using the arrival time of the final chunk in the batch.

## Transparent Forwarding of Interactive Input

During interactive capture (`interactive`), the host terminal is switched into Raw mode to receive keystrokes without local line buffering.
This prevents the host shell from intercepting arrow keys or Ctrl shortcuts, allowing them to be forwarded directly to the child process as escape sequences.
On Unix platforms where standard input is redirected, console2svg attempts to open `/dev/tty` directly to maintain interactive input.

When saving keystrokes for replay, the input byte stream is first written directly to the PTY and simultaneously parsed using a stateful UTF-8 decoder for recording.
If an escape sequence is truncated at the end of an input read, the remaining bytes are preserved across iterations, preventing incomplete fragments from being misrecorded as solitary keys (such as an isolated ESC).

## Draining Residual Output After Process Exit

A child process exit signal and the complete drainage of all bytes in the PTY buffer do not necessarily occur simultaneously.
Even after process termination, unread output frequently remains buffered within the operating system kernel.

Accordingly, console2svg keeps the output reader active for up to 500 milliseconds following process exit to thoroughly drain trailing data.
In addition, a 1-second timeout is enforced during process cleanup, ensuring that the CLI never hangs indefinitely if backend teardown encounters a deadlock.

## Fallback on PTY Initialization Failure

In restricted environments or specialized container configurations, native PTY backends may fail to initialize.
Alternatively, a PTY process may spawn but hang indefinitely without producing output.

console2svg detects unresponsiveness during startup and retries PTY initialization up to three times.
If a PTY still cannot be established, execution falls back to a standard process using redirected standard streams.
While this fallback cannot replicate TTY-dependent interactive features such as progress bars or full-screen TUIs, it guarantees that non-interactive command outputs remain recordable rather than failing completely.
