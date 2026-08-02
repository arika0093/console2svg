using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZLogger;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Records terminal output by repeatedly executing a command at regular intervals and
/// stitching the captured results into an animation.  Each execution is treated as a
/// full-screen refresh: the terminal is cleared before each frame's output is applied.
/// This is useful for commands like <c>tmux capture-pane -pe -t :0</c> that emit the
/// current state of a terminal pane as a static snapshot.
/// </summary>
public static class RepeatRecorder
{
    // ESC[0m = reset SGR style; ESC[2J = clear entire screen; ESC[H = move cursor to top-left.
    // Resetting style first prevents clear from inheriting the previous frame's background color.
    private const string ClearScreenSequence = "\x1b[0m\x1b[2J\x1b[H";

    // Remove some CI environment variables to avoid apps switching to no-colour mode.
    private static readonly string[] ShellDeletedEnvironmentKeys = ["CI", "TF_BUILD"];

    /// <summary>
    /// Repeatedly executes <paramref name="command"/> at intervals of
    /// <c>1 / <paramref name="fps"/></c> seconds and builds a
    /// <see cref="RecordingSession"/> suitable for animated SVG rendering.
    /// The loop runs until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public static async Task<RecordingSession> RecordAsync(
        string command,
        int width,
        int height,
        double fps,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        bool noDeleteEnvs = false
    )
    {
        logger ??= NullLogger.Instance;
        logger.ZLogDebug(
            $"Start repeat recording. Command={command} Width={width} Height={height} Fps={fps}"
        );

        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();
        var interval = fps > 0 ? 1.0 / fps : 1.0;
        var iteration = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var frameStart = stopwatch.Elapsed.TotalSeconds;
            logger.ZLogDebug($"Repeat iteration={iteration} frameStart={frameStart:F3}s");

            string output;
            try
            {
                output = await RunCommandAsync(command, noDeleteEnvs, cancellationToken, logger)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Each frame: clear the screen then apply the captured content so that
            // consecutive frames start from a clean slate.
            var normalizedOutput = NormalizeLineEndings(output);
            var trimmedOutput = TrimTrailingBlankLines(normalizedOutput);
            var eolCompletedOutput = EnsureEraseToEndOfLine(trimmedOutput);
            var frameData = ClearScreenSequence + eolCompletedOutput;
            session.AddEvent(frameStart, frameData);
            logger.ZLogDebug(
                $"Repeat iteration={iteration} outputChars={output.Length} frameDataChars={frameData.Length}"
            );

            iteration++;

            // Wait until the next scheduled interval.
            var elapsed = stopwatch.Elapsed.TotalSeconds - frameStart;
            var remaining = interval - elapsed;
            if (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(remaining), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.ZLogDebug(
            $"Repeat recording completed. Iterations={iteration} Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}"
        );
        return session;
    }

    private static async Task<string> RunCommandAsync(
        string command,
        bool noDeleteEnvs,
        CancellationToken cancellationToken,
        ILogger logger
    )
    {
        var startInfo = BuildProcessStartInfo(command, noDeleteEnvs);
        logger.ZLogDebug(
            $"Running repeat command. FileName={startInfo.FileName} Arguments={startInfo.Arguments}"
        );

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Register to kill the entire process tree if the token is cancelled while it is running.
        // Killing only the shell process can leave descendant processes alive and keep the stdout
        // pipe open, which would cause ReadToEndAsync to hang indefinitely.
        using var registration = cancellationToken.Register(() =>
        {
            // Let a short-lived command finish writing its already-started frame
            // before cancelling its process tree. This also prevents a timer that
            // fires during process startup from turning a valid snapshot into an
            // empty frame.
            _ = Task.Run(async () =>
            {
                await Task
                    .Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors killing the process.
                }
            }, CancellationToken.None);
        });

        // The process-tree cancellation registration above owns shutdown. Do not cancel
        // this read independently: a short recording timeout may otherwise discard
        // output that the child process already wrote before it exited.
        var output = await process
            .StandardOutput.ReadToEndAsync(CancellationToken.None)
            .ConfigureAwait(false);

        // WaitForExitAsync is available from .NET 5 and above.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        logger.ZLogDebug(
            $"Repeat command completed. ExitCode={process.ExitCode} OutputLength={output.Length}"
        );

        return output;
    }

    /// <summary>
    /// Ensures that bare <c>LF</c> characters are converted to <c>CR+LF</c> so the
    /// terminal emulator advances the cursor correctly.  Any <c>LF</c> already preceded
    /// by a <c>CR</c> is left as-is.
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 16);
        var previousWasCarriageReturn = false;
        foreach (var ch in text)
        {
            if (ch == '\n' && !previousWasCarriageReturn)
            {
                sb.Append('\r');
            }

            sb.Append(ch);
            previousWasCarriageReturn = ch == '\r';
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes trailing blank lines from the output to prevent excessive cursor
    /// movement that can cause content to scroll out of the visible terminal area.
    /// A line is considered blank if it contains only whitespace or ANSI escape
    /// sequences that don't produce visible content (e.g., ESC[K).
    /// </summary>
    private static string TrimTrailingBlankLines(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        // Split by CRLF and remove trailing blank lines
        var lines = text.Split(["\r\n"], StringSplitOptions.None);
        var lastNonBlankIndex = lines.Length - 1;

        // Find the last non-blank line
        while (lastNonBlankIndex >= 0 && IsBlankLine(lines[lastNonBlankIndex]))
        {
            lastNonBlankIndex--;
        }

        // If all lines are blank, return empty string
        if (lastNonBlankIndex < 0)
        {
            return string.Empty;
        }

        // Reconstruct the text up to the last non-blank line
        if (lastNonBlankIndex == lines.Length - 1)
        {
            // No trailing blank lines
            return text;
        }

        var sb = new StringBuilder();
        for (var i = 0; i <= lastNonBlankIndex; i++)
        {
            sb.Append(lines[i]);
            if (i < lastNonBlankIndex)
            {
                sb.Append("\r\n");
            }
        }

        // Add final CRLF if the original text ended with one
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Determines whether a line is blank (contains only whitespace or non-printing
    /// ANSI escape sequences).
    /// </summary>
    private static bool IsBlankLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return true;
        }

        // Check if the line contains only whitespace and ANSI escape sequences
        var i = 0;
        while (i < line.Length)
        {
            var ch = line[i];

            // Skip whitespace
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // Skip ANSI escape sequences
            if (ch == '\x1b' && i + 1 < line.Length && line[i + 1] == '[')
            {
                i += 2;
                // Skip parameter bytes (0x30-0x3F)
                while (i < line.Length && line[i] >= 0x30 && line[i] <= 0x3F)
                {
                    i++;
                }
                // Skip intermediate bytes (0x20-0x2F)
                while (i < line.Length && line[i] >= 0x20 && line[i] <= 0x2F)
                {
                    i++;
                }
                // Skip final byte (0x40-0x7E)
                if (i < line.Length && line[i] >= 0x40 && line[i] <= 0x7E)
                {
                    i++;
                }
                continue;
            }

            // Found a printable character
            return false;
        }

        return true;
    }

    /// <summary>
    /// Inserts <c>ESC[K</c> (erase to end of line) before each line break.
    /// Snapshot commands like <c>tmux capture-pane -pe</c> often trim trailing spaces,
    /// which drops right-edge background cells. Adding EL reconstructs line tails using
    /// the current SGR state for each captured line.
    /// </summary>
    private static string EnsureEraseToEndOfLine(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 64);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                sb.Append("\x1b[K");
                sb.Append("\r\n");
                i++;
                continue;
            }

            sb.Append(ch);
        }

        // Also complete the final line when output doesn't end with CRLF.
        if (!(text.EndsWith("\r\n", StringComparison.Ordinal)))
        {
            sb.Append("\x1b[K");
        }

        return sb.ToString();
    }

    private static ProcessStartInfo BuildProcessStartInfo(string command, bool noDeleteEnvs)
    {
        var shellCommand = BuildShellCommand(command, noDeleteEnvs);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = GetWindowsShellPath(),
                Arguments = "/d /c " + shellCommand + " 2>&1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments =
                "-c \""
                + (shellCommand + " 2>&1").Replace("\"", "\\\"", StringComparison.Ordinal)
                + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
    }

    private static string BuildShellCommand(string command, bool noDeleteEnvs)
    {
        if (noDeleteEnvs)
        {
            return command;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var clears = string.Join(
                " && ",
                ShellDeletedEnvironmentKeys.Select(key => $"set \"{key}=\"")
            );
            return clears + " && " + command;
        }

        return "unset " + string.Join(' ', ShellDeletedEnvironmentKeys) + "; " + command;
    }

    private static string GetWindowsShellPath()
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemDir, "cmd.exe");
    }
}
