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
        ReadOnlyMemory<byte> captureKey,
        bool video,
        bool noDeleteEnvs,
        Func<InteractiveCapture, Task> onCapture,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        if (captureKey.IsEmpty)
        {
            throw new ArgumentException("A capture key is required.", nameof(captureKey));
        }

        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var emulator = new TerminalEmulator(width, height, theme);
        var captureGate = new object();
        List<TerminalFrame>? videoFrames = null;
        var videoStarted = 0d;
        var stopwatch = Stopwatch.StartNew();

        var options = BuildOptions(width, height, noDeleteEnvs);
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

        async Task CaptureAsync()
        {
            InteractiveCapture capture;
            lock (captureGate)
            {
                if (!video)
                {
                    capture = new InteractiveCapture(emulator.Buffer.Clone());
                }
                else if (videoFrames is null)
                {
                    videoStarted = stopwatch.Elapsed.TotalSeconds;
                    videoFrames = [new TerminalFrame(0d, emulator.Buffer.Clone())];
                    return;
                }
                else
                {
                    capture = new InteractiveCapture(videoFrames);
                    videoFrames = null;
                }
            }

            await onCapture(capture).ConfigureAwait(false);
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
                                await outputWriter
                                    .WriteAsync(chars.AsMemory(0, charCount), lifetime.Token)
                                    .ConfigureAwait(false);
                                await outputWriter.FlushAsync(lifetime.Token).ConfigureAwait(false);
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
                var pending = new List<byte>(captureKey.Length);
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

                        for (var i = 0; i < count; i++)
                        {
                            pending.Add(bytes[i]);
                            while (pending.Count > 0)
                            {
                                if (IsPrefix(pending, captureKey.Span))
                                {
                                    if (pending.Count == captureKey.Length)
                                    {
                                        pending.Clear();
                                        try
                                        {
                                            await CaptureAsync().ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.ZLogError(ex, $"Interactive capture failed.");
                                            await Console.Error.WriteLineAsync(
                                                $"Interactive capture failed: {ex.Message}".AsMemory(),
                                                CancellationToken.None
                                            );
                                        }
                                    }
                                    break;
                                }

                                await connection.WriterStream
                                    .WriteAsync(new byte[] { pending[0] }, lifetime.Token)
                                    .ConfigureAwait(false);
                                pending.RemoveAt(0);
                            }
                        }

                        // F12 begins with ESC. Do not indefinitely swallow a standalone
                        // Escape key when an application receives it in a separate read.
                        if (pending.Count == 1 && pending[0] == 0x1b)
                        {
                            await connection.WriterStream
                                .WriteAsync(new byte[] { pending[0] }, lifetime.Token)
                                .ConfigureAwait(false);
                            pending.Clear();
                        }

                        await connection.WriterStream.FlushAsync(lifetime.Token).ConfigureAwait(false);
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

    private static NativePtyOptions BuildOptions(int width, int height, bool noDeleteEnvs)
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
                Args = ["/d", "/k"],
                Environment = environment,
                DisableInputEcho = false,
            };
        }

        var unixShell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(unixShell))
        {
            unixShell = "/bin/sh";
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
