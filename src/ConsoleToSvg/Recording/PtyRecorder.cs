using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pty.Net;

namespace ConsoleToSvg.Recording;

public static class PtyRecorder
{
    public static async Task<RecordingSession> RecordAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await RecordWithPtyAsync(command, width, height, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (
                ex is DllNotFoundException
                || ex is TypeInitializationException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException
            )
        {
            return await RecordWithProcessFallbackAsync(command, width, height, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<RecordingSession> RecordWithPtyAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        var options = BuildOptions(command, width, height);
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();

        using var connection = await PtyProvider.SpawnAsync(options, cancellationToken).ConfigureAwait(false);
        var readTask = ReadOutputAsync(connection.ReaderStream, session, stopwatch, cancellationToken);

        while (!connection.WaitForExit(50))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        await readTask.ConfigureAwait(false);
        return session;
    }

    private static async Task<RecordingSession> RecordWithProcessFallbackAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();

        var startInfo = BuildFallbackProcessStartInfo(command);
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.Start();

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

        await ReadOutputAsync(process.StandardOutput.BaseStream, session, stopwatch, cancellationToken).ConfigureAwait(false);
        while (!process.WaitForExit(50))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return session;
    }

    private static async Task ReadOutputAsync(
        Stream readerStream,
        RecordingSession session,
        Stopwatch stopwatch,
        CancellationToken cancellationToken
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
                break;
            }

            var charCount = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
            if (charCount <= 0)
            {
                continue;
            }

            var text = new string(chars, 0, charCount);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, text);
        }

        var trailingCount = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        if (trailingCount > 0)
        {
            var trailing = new string(chars, 0, trailingCount);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, trailing);
        }
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new PtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = "cmd.exe",
                CommandLine = new[] { "cmd.exe", "/d", "/c", command },
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
            CommandLine = new[] { "/bin/sh", "-lc", command },
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