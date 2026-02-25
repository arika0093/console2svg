using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pty.Net;
using ZLogger;

namespace ConsoleToSvg.Recording;

public static class PtyRecorder
{
    public static async Task<RecordingSession> RecordAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        logger ??= NullLogger.Instance;
        logger.ZLogDebug($"Start PTY recording. Command={command} Width={width} Height={height}");
        try
        {
            return await RecordWithPtyAsync(command, width, height, cancellationToken, logger).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (
                ex is DllNotFoundException
                || ex is TypeInitializationException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException
            )
        {
            logger.ZLogDebug(ex, $"PTY backend unavailable. Falling back to process execution.");
            return await RecordWithProcessFallbackAsync(command, width, height, cancellationToken, logger).ConfigureAwait(false);
        }
    }

    private static async Task<RecordingSession> RecordWithPtyAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger
    )
    {
        var options = BuildOptions(command, width, height);
        logger.ZLogDebug(
            $"Spawning PTY process. App={options.App} Args={string.Join(' ', options.CommandLine ?? [])} Cwd={options.Cwd} Cols={options.Cols} Rows={options.Rows}"
        );
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken).ConfigureAwait(false);
        logger.ZLogDebug($"PTY process spawned.");
        var readTask = ReadOutputAsync(connection.ReaderStream, session, stopwatch, cancellationToken, logger);

        try
        {
            while (!connection.WaitForExit(50))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // PTY process may have already exited; ignore cleanup errors such as
            // "Killing terminal failed with error 3" (ESRCH: no such process)
        }

        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch (IOException ex) when (IsExpectedPtyEof(ex))
        {
            // On Unix PTY, child exit can surface as EIO ("Input/output error")
            // when reading after the slave side is closed. Treat as EOF.
        }

        try
        {
            connection.Dispose();
        }
        catch
        {
            // Ignore disposal errors when the process has already exited
        }

        logger.ZLogDebug($"PTY recording completed. Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}");
        return session;
    }

    private static async Task<RecordingSession> RecordWithProcessFallbackAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger
    )
    {
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();

        var startInfo = BuildFallbackProcessStartInfo(command);
        logger.ZLogDebug(
            $"Using process fallback. FileName={startInfo.FileName} Arguments={startInfo.Arguments} Cwd={startInfo.WorkingDirectory}"
        );
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.Start();
        logger.ZLogDebug($"Fallback process started. Pid={process.Id}");

        using var registration = cancellationToken.Register(
            () =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Ignore cancellation kill failures.
                }
            }
        );

        await ReadOutputAsync(process.StandardOutput.BaseStream, session, stopwatch, cancellationToken, logger).ConfigureAwait(false);
        while (!process.WaitForExit(50))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        logger.ZLogDebug($"Fallback recording completed. ExitCode={process.ExitCode} Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}");
        return session;
    }

    private static bool IsExpectedPtyEof(IOException exception)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        return exception.Message.Contains("Input/output error", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ReadOutputAsync(
        Stream readerStream,
        RecordingSession session,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        ILogger logger
    )
    {
        var bytes = new byte[4096];
        var chars = new char[8192];
        var decoder = Encoding.UTF8.GetDecoder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await readerStream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            if (count <= 0)
            {
                logger.ZLogDebug($"Read stream completed (EOF).");
                break;
            }

            var charCount = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
            if (charCount <= 0)
            {
                logger.ZLogDebug($"Read chunk bytes={count}, no chars decoded yet.");
                continue;
            }

            var text = new string(chars, 0, charCount);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, text);
            logger.ZLogDebug(
                $"Captured chunk bytes={count} chars={charCount} elapsedMs={stopwatch.ElapsedMilliseconds} preview={ToPreview(text)}"
            );
        }

        var trailingCount = decoder.GetChars([], 0, 0, chars, 0, flush: true);
        if (trailingCount > 0)
        {
            var trailing = new string(chars, 0, trailingCount);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, trailing);
            logger.ZLogDebug($"Captured trailing decoded chars={trailingCount} preview={ToPreview(trailing)}");
        }
    }

    private static string ToPreview(string text)
    {
        var normalized = text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        return normalized.Length <= 120 ? normalized : normalized.Substring(0, 120) + "...";
    }

    private static PtyOptions BuildOptions(string command, int width, int height)
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        env["COLUMNS"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["LINES"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new PtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = "cmd.exe",
                CommandLine = ["/d", "/c", command],
                Environment = env,
            };
        }

        return new PtyOptions
        {
            Name = "console2svg",
            Cols = width,
            Rows = height,
            Cwd = Environment.CurrentDirectory,
            App = "/bin/sh",
            CommandLine = ["-lc", command],
            Environment = env,
        };
    }

    private static ProcessStartInfo BuildFallbackProcessStartInfo(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c " + command + " 2>&1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-lc \"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + " 2>&1\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
    }
}