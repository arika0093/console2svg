using System;
using System.Collections.Generic;
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

public static partial class PtyRecorder
{
    private static Stream? TryOpenInputForForwarding(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Console.IsInputRedirected)
        {
            var tty = TryOpenUnixTtyInput(logger);
            if (tty is not null)
            {
                return tty;
            }
        }

        return TryOpenStandardInput(logger);
    }

    private static Stream? TryOpenStandardInput(ILogger logger)
    {
        try
        {
            return Console.OpenStandardInput();
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Standard input is unavailable. Input forwarding is disabled.");
            return null;
        }
    }

    private static Stream? TryOpenUnixTtyInput(ILogger logger)
    {
        try
        {
            return new FileStream(
                "/dev/tty",
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                256,
                FileOptions.None
            );
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"/dev/tty is unavailable. Falling back to standard input.");
            return null;
        }
    }

    private static TextWriter? TryOpenStandardOutputWriter(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Console.IsOutputRedirected)
        {
            return null;
        }

        try
        {
            return Console.Out;
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(
                ex,
                $"Text output is unavailable. Falling back to stream output forwarding."
            );
            return null;
        }
    }

    private static Stream? TryOpenStandardOutput(ILogger logger)
    {
        try
        {
            return Console.OpenStandardOutput();
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Standard output is unavailable. Output forwarding is disabled.");
            return null;
        }
    }

    internal static void TryDisableTerminalMouseTracking(bool forwardToConsole, ILogger logger)
    {
        if (!forwardToConsole || Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            var output = TryOpenStandardOutput(logger);
            if (output is null)
            {
                return;
            }

            var bytes = Encoding.ASCII.GetBytes(DisableMouseTrackingSequence);
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
            logger.ZLogDebug($"Sent terminal mouse tracking reset sequence.");
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Failed to send terminal mouse tracking reset sequence.");
        }
    }

    private static string ToPreview(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\x");
                        builder.Append(
                            ((int)ch).ToString(
                                "X2",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        );
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static NativePtyOptions BuildOptions(
        ILogger logger,
        string command,
        int width,
        int height,
        bool disableInputEcho,
        bool noDeleteEnvs
    )
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
                logger.ZLogDebug($"Inherited environment variable: {key}={value}");
            }
        }

        logger.ZLogDebug($"Setting PTY size environment variables: COLUMNS={width} LINES={height}");
        env["COLUMNS"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["LINES"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var shellCommand = BuildShellCommand(command, noDeleteEnvs);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new NativePtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = "cmd.exe",
                Args = ["/d", "/c", shellCommand],
                Environment = env,
                DisableInputEcho = false,
            };
        }

        return new NativePtyOptions
        {
            Name = "console2svg",
            Cols = width,
            Rows = height,
            Cwd = Environment.CurrentDirectory,
            App = "/bin/sh",
            Args = ["-c", shellCommand],
            Environment = env,
            DisableInputEcho = disableInputEcho,
        };
    }

    private static ProcessStartInfo BuildFallbackProcessStartInfo(string command, bool noDeleteEnvs)
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
                RedirectStandardInput = true,
                RedirectStandardError = false,
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
            RedirectStandardInput = true,
            RedirectStandardError = false,
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

    internal sealed class ConsoleInputMode : IDisposable
    {
        private const uint StdInputHandle = 0xFFFFFFF6;
        private const uint EnableProcessedInput = 0x0001;
        private const uint EnableLineInput = 0x0002;
        private const uint EnableEchoInput = 0x0004;
        private const uint EnableMouseInput = 0x0010;
        private const uint EnableQuickEditMode = 0x0040;
        private const uint EnableExtendedFlags = 0x0080;
        private const uint EnableVirtualTerminalInput = 0x0200;

        private readonly ILogger _logger;
        private readonly IntPtr _handle;
        private readonly uint _originalMode;
        private readonly bool _changed;
        private readonly bool _isUnix;
        private readonly Termios _originalUnixTermios;

        private ConsoleInputMode(ILogger logger, IntPtr handle, uint originalMode)
        {
            _logger = logger;
            _handle = handle;
            _originalMode = originalMode;
            _changed = true;
            _isUnix = false;
            _originalUnixTermios = default;
        }

        private ConsoleInputMode(ILogger logger, Termios originalUnixTermios)
        {
            _logger = logger;
            _handle = IntPtr.Zero;
            _originalMode = 0;
            _changed = true;
            _isUnix = true;
            _originalUnixTermios = originalUnixTermios;
        }

        public static ConsoleInputMode? TryEnableRaw(ILogger logger)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryEnableUnixRaw(logger);
            }

            if (Console.IsInputRedirected)
            {
                return null;
            }

            try
            {
                var handle = GetStdHandle(StdInputHandle);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    return null;
                }

                if (!GetConsoleMode(handle, out var mode))
                {
                    return null;
                }

                var newMode = mode;
                newMode |= EnableVirtualTerminalInput | EnableExtendedFlags | EnableQuickEditMode;
                newMode &= ~(EnableLineInput | EnableEchoInput | EnableProcessedInput);
                // Keep host-side text selection available. Mouse reports, if a host
                // still emits them as VT input, are discarded by InteractiveRecorder.
                newMode &= ~EnableMouseInput;

                if (newMode == mode)
                {
                    return null;
                }

                if (!SetConsoleMode(handle, newMode))
                {
                    return null;
                }

                logger.ZLogDebug($"Enabled raw console input for PTY forwarding.");
                return new ConsoleInputMode(logger, handle, mode);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to enable raw console input.");
                return null;
            }
        }

        private static ConsoleInputMode? TryEnableUnixRaw(ILogger logger)
        {
            if (Console.IsInputRedirected)
            {
                return null;
            }

            try
            {
                const int stdinFd = 0;
                if (tcgetattr(stdinFd, out var termios) != 0)
                {
                    logger.ZLogDebug(
                        $"tcgetattr failed while enabling raw terminal input. errno={Marshal.GetLastWin32Error()}"
                    );
                    return null;
                }

                var raw = termios;
                // Let libc select the platform's complete raw-mode bit set. The
                // previous hand-maintained flags worked on some Unix terminals but
                // left WSL input in a cooked mode, so Ctrl keys and F-key VT
                // sequences were consumed before the PTY forwarder could read them.
                cfmakeraw(ref raw);
                raw.c_cc[VMIN] = 1;
                raw.c_cc[VTIME] = 0;
                if (tcsetattr(stdinFd, TCSANOW, ref raw) != 0)
                {
                    logger.ZLogDebug(
                        $"tcsetattr failed while enabling raw terminal input. errno={Marshal.GetLastWin32Error()}"
                    );
                    return null;
                }

                logger.ZLogDebug($"Enabled raw console input for PTY forwarding.");
                return new ConsoleInputMode(logger, termios);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to enable raw console input.");
                return null;
            }
        }

        public void Dispose()
        {
            if (!_changed)
            {
                return;
            }

            try
            {
                if (_isUnix)
                {
                    const int stdinFd = 0;
                    var original = _originalUnixTermios;
                    tcsetattr(stdinFd, TCSANOW, ref original);
                }
                else
                {
                    SetConsoleMode(_handle, _originalMode);
                }

                _logger.ZLogDebug($"Restored console input mode.");
            }
            catch
            {
                // Ignore restore failures.
            }
        }

        private const int TCSANOW = 0;
        private const int VTIME = 5;
        private const int VMIN = 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct Termios
        {
            public uint c_iflag;
            public uint c_oflag;
            public uint c_cflag;
            public uint c_lflag;
            public byte c_line;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] c_cc;

            public uint c_ispeed;
            public uint c_ospeed;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int tcgetattr(int fd, out Termios termios);

        [DllImport("libc", SetLastError = true)]
        private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios);

        [DllImport("libc")]
        private static extern void cfmakeraw(ref Termios termios);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(uint nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
