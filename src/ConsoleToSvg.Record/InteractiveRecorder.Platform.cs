using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Recording;

public static partial class InteractiveRecorder
{
    private static async Task ClearHostTerminalAsync(Stream output, SemaphoreSlim outputGate)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        await outputGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var clear = "\u001b[2J\u001b[H";
            await output
                .WriteAsync(Encoding.ASCII.GetBytes(clear), CancellationToken.None)
                .ConfigureAwait(false);
            await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            outputGate.Release();
        }
    }

    public static InteractiveCapture CompleteRecording(
        List<TerminalFrame> frames,
        double elapsedSeconds,
        ScreenBuffer finalScreen
    )
    {
        frames.Add(new TerminalFrame(elapsedSeconds, finalScreen.Clone()));
        return new InteractiveCapture(frames.ToArray());
    }

    private static int ReadUnixTerminalInput(byte[] buffer, int timeoutMilliseconds)
    {
        var descriptors = new[]
        {
            new PollFd { FileDescriptor = 0, Events = PollIn },
        };
        var pollResult = poll(descriptors, (nuint)descriptors.Length, timeoutMilliseconds);
        if (pollResult == 0)
        {
            return -1;
        }
        if (pollResult < 0)
        {
            var error = Marshal.GetLastWin32Error();
            return error is 4 or 11 ? -1 : throw new IOException($"poll failed: errno {error}");
        }

        var count = read(0, buffer, (nuint)buffer.Length);
        if (count >= 0)
        {
            return checked((int)count);
        }

        var readError = Marshal.GetLastWin32Error();
        return readError is 4 or 11 ? -1 : throw new IOException($"read failed: errno {readError}");
    }

    private const short PollIn = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int FileDescriptor;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(PollFd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nuint count);

    public sealed class HostTerminalSequenceFilter
    {
        private static readonly HashSet<string> SuppressedPrivateModes =
        [
            "9",
            "1000",
            "1002",
            "1003",
            "1004",
            "1005",
            "1006",
            "1015",
            "1016",
            "9001",
        ];
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
                while (
                    end < _pending.Length && (char.IsDigit(_pending[end]) || _pending[end] == ';')
                )
                {
                    end++;
                }

                if (end >= _pending.Length)
                {
                    break;
                }

                if (_pending[end] is 'h' or 'l')
                {
                    var modes = _pending.ToString(index + 3, end - index - 3).Split(';');
                    var retainedModes = modes
                        .Where(mode => !SuppressedPrivateModes.Contains(mode))
                        .ToArray();
                    if (retainedModes.Length == modes.Length)
                    {
                        output.Append(_pending[index++]);
                        continue;
                    }

                    if (retainedModes.Length > 0)
                    {
                        output.Append("\u001b[?");
                        output.Append(string.Join(";", retainedModes));
                        output.Append(_pending[end]);
                    }
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
            // WSL does not always propagate SHELL to a launched .NET process.
            // Prefer Bash so Ctrl+L/Ctrl+D retain the familiar interactive bindings.
            unixShell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
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
