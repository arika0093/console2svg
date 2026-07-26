using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Recording;

public sealed class InteractiveCapture
{
    public InteractiveCapture(ScreenBuffer screen)
    {
        Screen = screen;
        Frames = [];
    }

    public InteractiveCapture(IReadOnlyList<TerminalFrame> frames)
    {
        Frames = frames;
        Screen = frames[0].Buffer;
    }

    public ScreenBuffer Screen { get; }

    public IReadOnlyList<TerminalFrame> Frames { get; }

    public bool IsVideo => Frames.Count > 0;
}

/// <summary>Runs an interactive shell in a PTY and emits snapshots without sending capture keys to it.</summary>
public static class InteractiveRecorder
{
    public static async Task RunAsync(
        int width,
        int height,
        Theme theme,
        ReadOnlyMemory<byte> screenshotKey,
        ReadOnlyMemory<byte> recordingKey,
        bool noDeleteEnvs,
        string[]? command,
        Func<InteractiveCapture, Task<string?>> onCapture,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        if (screenshotKey.IsEmpty || recordingKey.IsEmpty)
        {
            throw new ArgumentException("Screenshot and recording keys are required.");
        }

        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var emulator = new TerminalEmulator(width, height, theme);
        var captureGate = new object();
        using var hostOutputGate = new SemaphoreSlim(1, 1);
        List<TerminalFrame>? videoFrames = null;
        var videoStarted = 0d;
        var stopwatch = Stopwatch.StartNew();

        var options = BuildOptions(width, height, noDeleteEnvs, command);
        using var connection = await NativePty
            .SpawnAsync(options, cancellationToken)
            .ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var rawInput = PtyRecorder.ConsoleInputMode.TryEnableRaw(logger);
        var output = Console.OpenStandardOutput();
        var outputWriter =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Console.IsOutputRedirected
                ? Console.Out
                : null;
        var input = Console.OpenStandardInput();
        PtyRecorder.TryDisableTerminalMouseTracking(forwardToConsole: true, logger);

        async Task NotifyAsync(string message)
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                // This is deliberately written to the host terminal, never the PTY.
                // Saving/restoring the cursor prevents it from becoming shell input.
                var width = Math.Max(20, Console.WindowWidth);
                var label = $"  {message}  ";
                if (label.Length > width - 2)
                {
                    label = label[..Math.Max(1, width - 2)];
                }

                var column = Math.Max(1, width - label.Length);
                var overlay = $"\u001b7\u001b[1;{column}H\u001b[30;48;5;114m{label}\u001b[0m\u001b8";
                await output.WriteAsync(Encoding.UTF8.GetBytes(overlay), lifetime.Token).ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(1500), lifetime.Token).ConfigureAwait(false);
                var clear = $"\u001b7\u001b[1;{column}H{new string(' ', label.Length)}\u001b8";
                await output.WriteAsync(Encoding.UTF8.GetBytes(clear), lifetime.Token).ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                hostOutputGate.Release();
            }
        }

        async Task SaveCaptureAsync(InteractiveCapture capture)
        {
            var message = await onCapture(capture).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(message))
            {
                await NotifyAsync(message).ConfigureAwait(false);
            }
        }

        async Task CaptureScreenshotAsync()
        {
            InteractiveCapture capture;
            lock (captureGate)
            {
                capture = new InteractiveCapture(emulator.Buffer.Clone());
            }

            await SaveCaptureAsync(capture).ConfigureAwait(false);
        }

        async Task ToggleRecordingAsync()
        {
            InteractiveCapture? capture = null;
            lock (captureGate)
            {
                if (videoFrames is null)
                {
                    videoStarted = stopwatch.Elapsed.TotalSeconds;
                    videoFrames = [new TerminalFrame(0d, emulator.Buffer.Clone())];
                }
                else
                {
                    capture = new InteractiveCapture(videoFrames);
                    videoFrames = null;
                }
            }

            if (capture is null)
            {
                await NotifyAsync("Recording started").ConfigureAwait(false);
                return;
            }

            await SaveCaptureAsync(capture).ConfigureAwait(false);
        }

        var outputTask = Task.Run(
            async () =>
            {
                var bytes = new byte[4096];
                var chars = new char[8192];
                var outputEncoding = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Console.OutputEncoding
                    : Encoding.UTF8;
                var decoder = outputEncoding.GetDecoder();
                var hostSequenceFilter = new HostTerminalSequenceFilter();
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        var count = await connection.ReaderStream
                            .ReadAsync(bytes, 0, bytes.Length, lifetime.Token)
                            .ConfigureAwait(false);
                        if (count <= 0)
                        {
                            break;
                        }

                        var charCount = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
                        lock (captureGate)
                        {
                            if (charCount > 0)
                            {
                                var text = new string(chars, 0, charCount);
                                emulator.Process(text);
                                if (videoFrames is not null)
                                {
                                    videoFrames.Add(
                                        new TerminalFrame(
                                            stopwatch.Elapsed.TotalSeconds - videoStarted,
                                            emulator.Buffer.Clone()
                                        )
                                    );
                                }
                            }
                        }

                        if (outputWriter is not null)
                        {
                            if (charCount > 0)
                            {
                                var text = hostSequenceFilter.Filter(new string(chars, 0, charCount));
                                if (text.Length > 0)
                                {
                                    await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                    try
                                    {
                                        await outputWriter
                                            .WriteAsync(text.AsMemory(), lifetime.Token)
                                            .ConfigureAwait(false);
                                        await outputWriter.FlushAsync(lifetime.Token).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        hostOutputGate.Release();
                                    }
                                }
                            }
                        }
                        else
                        {
                            await output
                                .WriteAsync(bytes, 0, count, lifetime.Token)
                                .ConfigureAwait(false);
                            await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown.
                }
                catch (IOException) when (lifetime.IsCancellationRequested)
                {
                    // Closing a Unix PTY may surface as EIO.
                }
            },
            CancellationToken.None
        );

        var inputTask = Task.Run(
            async () =>
            {
                var bytes = new byte[256];
                var pending = new List<byte>(Math.Max(screenshotKey.Length, recordingKey.Length));
                var discardingSgrMouseReport = false;
                var inputGate = new SemaphoreSlim(1, 1);
                long escapePendingVersion = 0;

                void ScheduleStandaloneEscape(long version)
                {
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await Task.Delay(30, lifetime.Token).ConfigureAwait(false);
                                await inputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                try
                                {
                                    if (
                                        Volatile.Read(ref escapePendingVersion) == version
                                        && pending.Count == 1
                                        && pending[0] == 0x1b
                                    )
                                    {
                                        await connection.WriterStream
                                            .WriteAsync(pending.ToArray(), lifetime.Token)
                                            .ConfigureAwait(false);
                                        pending.Clear();
                                        await connection.WriterStream
                                            .FlushAsync(lifetime.Token)
                                            .ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    inputGate.Release();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // The session ended before a standalone Escape was due.
                            }
                        },
                        CancellationToken.None
                    );
                }

                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        var count = await input
                            .ReadAsync(bytes, 0, bytes.Length, lifetime.Token)
                            .ConfigureAwait(false);
                        if (count <= 0)
                        {
                            break;
                        }

                        await inputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        try
                        {
                            // A new byte invalidates any pending standalone-Escape timer.
                            Interlocked.Increment(ref escapePendingVersion);
                            for (var i = 0; i < count; i++)
                            {
                                if (discardingSgrMouseReport)
                                {
                                    if (bytes[i] is (byte)'M' or (byte)'m')
                                    {
                                        discardingSgrMouseReport = false;
                                    }

                                    continue;
                                }

                                pending.Add(bytes[i]);
                                while (pending.Count > 0)
                                {
                                    if (IsPrefix(pending, screenshotKey.Span) || IsPrefix(pending, recordingKey.Span))
                                    {
                                        if (pending.Count == screenshotKey.Length && IsPrefix(pending, screenshotKey.Span))
                                        {
                                            pending.Clear();
                                            try
                                            {
                                                await CaptureScreenshotAsync().ConfigureAwait(false);
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.ZLogError(ex, $"Interactive capture failed.");
                                            }
                                        }
                                        else if (pending.Count == recordingKey.Length && IsPrefix(pending, recordingKey.Span))
                                        {
                                            pending.Clear();
                                            try
                                            {
                                                await ToggleRecordingAsync().ConfigureAwait(false);
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.ZLogError(ex, $"Interactive recording capture failed.");
                                            }
                                        }
                                        break;
                                    }

                                    // ENABLE_VIRTUAL_TERMINAL_INPUT can surface host mouse
                                    // reports as CSI <... M/m. They are neither shell input
                                    // nor capture keys; forwarding them produces visible
                                    // fragments such as "[<35;..." in line editors.
                                    if (IsSgrMouseReportPrefix(pending))
                                    {
                                        pending.Clear();
                                        discardingSgrMouseReport = true;
                                        break;
                                    }

                                    // Any non-capture sequence is opaque input. Forward
                                    // it as received rather than special-casing cursor
                                    // keys, paste markers, modifiers, or future VT keys.
                                    await connection.WriterStream
                                        .WriteAsync(pending.ToArray(), lifetime.Token)
                                        .ConfigureAwait(false);
                                    pending.Clear();
                                }
                            }

                            // Function keys and other VT input can arrive across reads.
                            // Give a lone Escape a short grace period before forwarding it.
                            if (pending.Count == 1 && pending[0] == 0x1b)
                            {
                                var version = Interlocked.Increment(ref escapePendingVersion);
                                ScheduleStandaloneEscape(version);
                            }

                            await connection.WriterStream.FlushAsync(lifetime.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            inputGate.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown.
                }
                catch (IOException)
                {
                    // The shell exited while input was being forwarded.
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, $"Interactive input or capture failed.");
                    await Console.Error.WriteLineAsync(
                        $"Interactive capture failed: {ex.Message}".AsMemory(),
                        CancellationToken.None
                    );
                }
            },
            CancellationToken.None
        );

        try
        {
            while (!cancellationToken.IsCancellationRequested && !outputTask.IsCompleted)
            {
                if (connection.WaitForExit(50))
                {
                    break;
                }
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            connection.Dispose();
            await IgnoreFailureAsync(outputTask).ConfigureAwait(false);
            await IgnoreFailureAsync(inputTask).ConfigureAwait(false);
            // A child application can leave the outer terminal in mouse-reporting
            // mode. Reset it before restoring the host input mode so selection is
            // available after an interactive session ends.
            PtyRecorder.TryDisableTerminalMouseTracking(forwardToConsole: true, logger);
        }
    }

    private static bool IsPrefix(List<byte> value, ReadOnlySpan<byte> expected)
    {
        if (value.Count > expected.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Count; i++)
        {
            if (value[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSgrMouseReportPrefix(List<byte> value) =>
        value.Count == 3 && value[0] == 0x1b && value[1] == (byte)'[' && value[2] == (byte)'<';

    private sealed class HostTerminalSequenceFilter
    {
        private static readonly HashSet<string> SuppressedPrivateModes =
            ["9", "1000", "1002", "1003", "1004", "1005", "1006", "1015", "1016", "9001"];
        private readonly StringBuilder _pending = new();

        public string Filter(string text)
        {
            _pending.Append(text);
            var output = new StringBuilder(_pending.Length);
            var index = 0;
            while (index < _pending.Length)
            {
                if (_pending[index] != '\u001b')
                {
                    output.Append(_pending[index++]);
                    continue;
                }

                if (index + 2 >= _pending.Length)
                {
                    break;
                }

                if (_pending[index + 1] != '[' || _pending[index + 2] != '?')
                {
                    output.Append(_pending[index++]);
                    continue;
                }

                var end = index + 3;
                while (end < _pending.Length && char.IsDigit(_pending[end]))
                {
                    end++;
                }

                if (end >= _pending.Length)
                {
                    break;
                }

                var mode = _pending.ToString(index + 3, end - index - 3);
                if ((_pending[end] is 'h' or 'l') && SuppressedPrivateModes.Contains(mode))
                {
                    index = end + 1;
                    continue;
                }

                output.Append(_pending[index++]);
            }

            _pending.Remove(0, index);
            return output.ToString();
        }
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // PTY shutdown races are expected.
        }
    }

    private static NativePtyOptions BuildOptions(
        int width,
        int height,
        bool noDeleteEnvs,
        string[]? command
    )
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                environment[key] = value;
            }
        }

        environment["COLUMNS"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment["LINES"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!noDeleteEnvs)
        {
            environment.Remove("CI");
            environment.Remove("TF_BUILD");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (command is { Length: > 0 })
            {
                return new NativePtyOptions
                {
                    Name = "console2svg",
                    Cols = width,
                    Rows = height,
                    Cwd = Environment.CurrentDirectory,
                    App = command[0],
                    Args = command[1..],
                    Environment = environment,
                    DisableInputEcho = false,
                };
            }

            var shell = Environment.GetEnvironmentVariable("COMSPEC");
            if (string.IsNullOrWhiteSpace(shell))
            {
                shell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cmd.exe"
                );
            }

            return new NativePtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = shell,
                // Do not use cmd.exe's /d switch here: it disables the user's
                // AutoRun configuration, including prompt integrations such as
                // Starship. An interactive capture should behave like their shell.
                Args = ["/k"],
                Environment = environment,
                DisableInputEcho = false,
            };
        }

        var unixShell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(unixShell))
        {
            unixShell = "/bin/sh";
        }

        if (command is { Length: > 0 })
        {
            return new NativePtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = command[0],
                Args = command[1..],
                Environment = environment,
                DisableInputEcho = false,
            };
        }

        return new NativePtyOptions
        {
            Name = "console2svg",
            Cols = width,
            Rows = height,
            Cwd = Environment.CurrentDirectory,
            App = unixShell,
            Args = ["-i"],
            Environment = environment,
            DisableInputEcho = false,
        };
    }
}
