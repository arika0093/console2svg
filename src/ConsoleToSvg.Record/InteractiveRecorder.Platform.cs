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
using Porta.Pty;
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

    /// <summary>
    /// Returns true when the PTY root process currently has a nested child shell
    /// (Windows only). Ctrl+D (EOT) is always forwarded to the PTY; the caller uses
    /// this to decide whether EOT belongs to the active nested shell (forward only)
    /// or to the top-level shell, which ignores EOT on Windows (exit the session).
    /// Failures conservatively report nested so a nested shell is never killed.
    /// </summary>
    public static bool HasNestedChildProcesses(int rootProcessId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || rootProcessId <= 0)
        {
            return true;
        }

        const uint TH32CS_SNAPPROCESS = 0x2;
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return true;
        }

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return true;
            }

            do
            {
                if (
                    entry.th32ParentProcessID == rootProcessId
                    && entry.th32ProcessID != rootProcessId
                    && IsShellChildProcess(entry.szExeFile)
                )
                {
                    return true;
                }
            } while (Process32Next(snapshot, ref entry));
            return false;
        }
        catch
        {
            return true;
        }
        finally
        {
            CloseToolhelpSnapshot(snapshot);
        }
    }

    /// <summary>
    /// Returns true when a child process image counts as a nested shell for Ctrl+D
    /// purposes. The ConPTY host (conhost.exe) is attached to the pseudo-console
    /// machinery rather than being a user shell, so it never counts.
    /// </summary>
    public static bool IsShellChildProcess(string? exeFile)
    {
        if (string.IsNullOrWhiteSpace(exeFile))
        {
            return false;
        }

        return !exeFile.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase)
            && !exeFile.Equals("conhost", StringComparison.OrdinalIgnoreCase);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    private static extern bool CloseToolhelpSnapshot(IntPtr hObject);

    public sealed class HostTerminalSequenceFilter
    {
        // Passthrough is enabled by --mouse (see AppOptions.Mouse).
        private readonly bool _mousePassthrough;
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

        public HostTerminalSequenceFilter(bool mousePassthrough = false)
        {
            _mousePassthrough = mousePassthrough;
        }

        public string Filter(string text)
        {
            _pending.Append(text);
            if (_mousePassthrough)
            {
                var passthrough = _pending.ToString();
                _pending.Clear();
                return passthrough;
            }
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

    private static PtyOptions BuildOptions(
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
        environment["DOTNET_EnableWriteXorExecute"] = "0";
        if (!noDeleteEnvs)
        {
            environment.Remove("CI");
            environment.Remove("TF_BUILD");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (command is { Length: > 0 })
            {
                return new PtyOptions
                {
                    Name = "console2svg",
                    Cols = width,
                    Rows = height,
                    Cwd = Environment.CurrentDirectory,
                    App = command[0],
                    CommandLine = PtyCommandLine.QuoteArgs(command[0], command[1..]),
                    VerbatimCommandLine = true,
                    Environment = environment,
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

            return new PtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = shell,
                // Do not use cmd.exe's /d switch here: it disables the user's
                // AutoRun configuration, including prompt integrations such as
                // Starship. An interactive capture should behave like their shell.
                CommandLine = PtyCommandLine.QuoteArgs(shell, ["/k"]),
                VerbatimCommandLine = true,
                Environment = environment,
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
            return new PtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = command[0],
                CommandLine = command[1..],
                Environment = environment,
            };
        }

        return new PtyOptions
        {
            Name = "console2svg",
            Cols = width,
            Rows = height,
            Cwd = Environment.CurrentDirectory,
            App = unixShell,
            CommandLine = ["-i"],
            Environment = environment,
        };
    }
}
