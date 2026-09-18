---
title: Starting, controlling, and capturing output from a PTY
description: How console2svg prepares a child-process environment close to a real terminal and records output that includes screen control.
---

Simply connecting standard output to a pipe is not enough for terminal recording. Many CLIs change colors, progress display, line buffering, and interactive UI depending on whether they are attached to a TTY. TUIs also emit cursor movement and screen clearing, so a simple line log cannot reproduce the final screen. Therefore, `PtyRecorder` starts an OS-native PTY and provides input/output that looks close to a real terminal from the child process.

## Recording path

At startup, the column and row counts are passed to the PTY, and the same values are put into the `COLUMNS` and `LINES` environment variables. The child process is executed through `cmd.exe /d /c` on Windows and `/bin/sh -c` on Unix-like systems. The parent process reads PTY output and adds it to `RecordingSession` paired with elapsed stopwatch time. The later ANSI parser replays this event sequence, so it is important not to format by line here and to preserve the output order including control sequences.

In interactive capture, user input is forwarded to the PTY while output is also forwarded to the original console as needed. On Unix, even if standard input is redirected for another purpose, operation continues if `/dev/tty` can be opened. At exit, mouse tracking reset sequences are sent. This prevents full-screen application state from remaining in the user's terminal; it is not a process that modifies the recording data itself.

## Do not treat process exit as immediate disconnection

Child process exit and the end of PTY output are not necessarily simultaneous. Even after detecting exit, console2svg takes up to 500 ms of drain time to record final output remaining in kernel buffers.

On Unix, reads after the slave side is closed may appear as `EIO` instead of EOF, so this is treated as normal termination.

For timeouts and cancellations, partial recordings are finalized, and PTY disposal and read stopping each have a 1-second upper limit. Cleanup itself must not continue stopping the recording command, so cleanup completion is not waited for indefinitely.

A state with no output after startup is also detected, and after short retries console2svg falls back to a normal redirected process. This is a choice to proceed with conversion, even with limited functionality, rather than “hang with no output” in environments where PTY is unavailable.
