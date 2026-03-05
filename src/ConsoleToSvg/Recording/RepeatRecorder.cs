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
            var eolCompletedOutput = EnsureEraseToEndOfLine(normalizedOutput);
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
        });

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);

        // WaitForExitAsync is available from .NET 5 and above.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        logger.ZLogDebug(
            $"Repeat command completed. ExitCode={process.ExitCode} OutputLength={output.Length}"
        );

        cancellationToken.ThrowIfCancellationRequested();
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
                FileName = "cmd.exe",
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
}
