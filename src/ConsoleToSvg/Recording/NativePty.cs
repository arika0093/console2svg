using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Pty.Net;

namespace ConsoleToSvg.Recording;

internal sealed class NativePtyOptions
{
    public string? Name { get; init; }
    public int Cols { get; init; }
    public int Rows { get; init; }
    public string? Cwd { get; init; }
    public string App { get; init; } = "";
    public string[]? Args { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

internal static class NativePty
{
    public static Task<NativePtyConnection> SpawnAsync(
        NativePtyOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(NativePtyWindows.Spawn(options));
        }

        return NativePtyUnix.SpawnAsync(options, cancellationToken);
    }
}

internal sealed class NativePtyConnection : IDisposable
{
    private readonly Action _dispose;
    private readonly Func<int, bool> _waitForExit;

    public NativePtyConnection(
        Stream readerStream,
        Stream writerStream,
        Func<int, bool> waitForExit,
        Action dispose
    )
    {
        ReaderStream = readerStream;
        WriterStream = writerStream;
        _waitForExit = waitForExit;
        _dispose = dispose;
    }

    public Stream ReaderStream { get; }
    public Stream WriterStream { get; }

    public bool WaitForExit(int milliseconds) => _waitForExit(milliseconds);

    public void Dispose() => _dispose();
}

internal static class NativePtyWindows
{
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_FAILED = 0xFFFFFFFF;

    public static NativePtyConnection Spawn(NativePtyOptions options)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("ConPTY is only available on Windows.");
        }

        var size = new Coord((short)options.Cols, (short)options.Rows);
        EnsureWin32(CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0));
        EnsureWin32(CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0));

        EnsureWin32(SetHandleInformation(inputWrite, HANDLE_FLAG_INHERIT, 0));
        EnsureWin32(SetHandleInformation(outputRead, HANDLE_FLAG_INHERIT, 0));

        var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out var hPC);
        if (hr != 0)
        {
            ThrowWin32("CreatePseudoConsole failed", hr);
        }

        CloseHandle(inputRead);
        CloseHandle(outputWrite);

        IntPtr lpAttributeList = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        try
        {
            var startupInfo = new StartupInfoEx();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, out var attrListSize);
            lpAttributeList = Marshal.AllocHGlobal(attrListSize);
            if (!InitializeProcThreadAttributeList(lpAttributeList, 1, 0, out attrListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (
                !UpdateProcThreadAttribute(
                    lpAttributeList,
                    0,
                    (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    hPC,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero
                )
            )
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            startupInfo.lpAttributeList = lpAttributeList;

            var commandLine = BuildCommandLine(options.App, options.Args);
            environmentBlock = BuildEnvironmentBlock(options.Environment);
            var cwd = string.IsNullOrWhiteSpace(options.Cwd) ? null : options.Cwd;
            var processInfo = CreateProcessWithRetry(
                commandLine,
                environmentBlock,
                cwd,
                ref startupInfo
            );

            var reader = new FileStream(
                new SafeFileHandle(outputRead, ownsHandle: true),
                FileAccess.Read,
                4096,
                isAsync: false
            );
            var writer = new FileStream(
                new SafeFileHandle(inputWrite, ownsHandle: true),
                FileAccess.Write,
                4096,
                isAsync: false
            );

            var exited = false;

            bool WaitForExit(int milliseconds)
            {
                if (exited)
                {
                    return true;
                }

                var result = WaitForSingleObject(processInfo.hProcess, milliseconds);
                if (result == WAIT_OBJECT_0)
                {
                    exited = true;
                    return true;
                }

                if (result == WAIT_FAILED)
                {
                    exited = true;
                    return true;
                }

                return false;
            }

            void Dispose()
            {
                try
                {
                    reader.Dispose();
                }
                catch
                {
                    // Best-effort cleanup; ignore failures.
                }

                try
                {
                    writer.Dispose();
                }
                catch
                {
                    // Best-effort cleanup; ignore failures.
                }

                if (!exited)
                {
                    TryGracefulExit(processInfo.hProcess, ref exited);
                }

                ClosePseudoConsole(hPC);
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
            }

            return new NativePtyConnection(reader, writer, WaitForExit, Dispose);
        }
        catch
        {
            try
            {
                ClosePseudoConsole(hPC);
            }
            catch
            {
                // Best-effort cleanup; ignore failures.
            }

            try
            {
                CloseHandle(inputRead);
                CloseHandle(inputWrite);
                CloseHandle(outputRead);
                CloseHandle(outputWrite);
            }
            catch
            {
                // Best-effort cleanup; ignore failures.
            }

            throw;
        }
        finally
        {
            if (lpAttributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(lpAttributeList);
                Marshal.FreeHGlobal(lpAttributeList);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
        }
    }

    private static string BuildCommandLine(string app, string[]? args)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteWindowsArg(app));
        if (args is not null)
        {
            foreach (var arg in args)
            {
                builder.Append(' ');
                builder.Append(QuoteWindowsArg(arg));
            }
        }

        return builder.ToString();
    }

    private static string QuoteWindowsArg(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var needsQuotes = value.IndexOfAny([' ', '\t', '"']) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(ch);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? env)
    {
        if (env is null || env.Count == 0)
        {
            return IntPtr.Zero;
        }

        var builder = new StringBuilder();
        var entries = new List<KeyValuePair<string, string>>(env.Count);
        foreach (var pair in env)
        {
            entries.Add(pair);
        }

        entries.Sort(
            (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key)
        );

        foreach (var pair in entries)
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private static void EnsureWin32(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static ProcessInformation CreateProcessWithRetry(
        string commandLine,
        IntPtr environmentBlock,
        string? cwd,
        ref StartupInfoEx startupInfo
    )
    {
        if (
            CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                environmentBlock,
                cwd,
                ref startupInfo,
                out var processInfo
            )
        )
        {
            return processInfo;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == 87 && environmentBlock != IntPtr.Zero)
        {
            if (
                CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero,
                    cwd,
                    ref startupInfo,
                    out processInfo
                )
            )
            {
                return processInfo;
            }

            error = Marshal.GetLastWin32Error();
        }

        throw new Win32Exception(error);
    }

    private static void TryGracefulExit(IntPtr processHandle, ref bool exited)
    {
        try
        {
            var waitResult = WaitForSingleObject(processHandle, 500);
            if (waitResult == 0)
            {
                exited = true;
                return;
            }
        }
        catch
        {
            // Best-effort check; ignore failures.
        }

        try
        {
            TerminateProcess(processHandle, 1);
        }
        catch
        {
            // Best-effort termination; ignore failures.
        }
    }

    private static void ThrowWin32(string message, int hr)
    {
        throw new Win32Exception(hr, message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;

        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        Coord size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr phPC
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        out IntPtr lpSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

internal static class NativePtyUnix
{
    public static async Task<NativePtyConnection> SpawnAsync(
        NativePtyOptions options,
        CancellationToken cancellationToken
    )
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Quick.PtyNet Unix backend is not available on Windows."
            );
        }

        var environment = new Dictionary<string, string>();
        if (options.Environment is not null)
        {
            foreach (var pair in options.Environment)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        var ptyOptions = new PtyOptions
        {
            Name = options.Name ?? "console2svg",
            Cols = options.Cols,
            Rows = options.Rows,
            Cwd = string.IsNullOrWhiteSpace(options.Cwd)
                ? Environment.CurrentDirectory
                : options.Cwd,
            App = options.App,
            CommandLine = options.Args ?? Array.Empty<string>(),
            Environment = environment,
        };

        var pty = await PtyProvider.SpawnAsync(ptyOptions, cancellationToken).ConfigureAwait(false);

        // Disable PTY slave ECHO to prevent echoed input bytes from being captured
        // in the recording output with ECHOCTL caret-notation (e.g. ESC → "^[").
        TryDisablePtyEcho(pty.WriterStream);

        var exited = false;
        void OnProcessExited(object? sender, PtyExitedEventArgs eventArgs)
        {
            exited = true;
        }

        pty.ProcessExited += OnProcessExited;

        bool WaitForExit(int milliseconds)
        {
            if (exited)
            {
                return true;
            }

            try
            {
                if (pty.WaitForExit(milliseconds))
                {
                    exited = true;
                    return true;
                }
            }
            catch
            {
                exited = true;
                return true;
            }

            return false;
        }

        void Dispose()
        {
            pty.ProcessExited -= OnProcessExited;

            if (!exited)
            {
                try
                {
                    pty.Kill();
                }
                catch
                {
                    // Best-effort kill; ignore failures.
                }

                try
                {
                    exited = pty.WaitForExit(500) || exited;
                }
                catch
                {
                    // Best-effort wait; ignore failures.
                }
            }

            try
            {
                pty.ReaderStream.Dispose();
            }
            catch
            {
                // Best-effort cleanup; ignore failures.
            }

            try
            {
                pty.WriterStream.Dispose();
            }
            catch
            {
                // Best-effort cleanup; ignore failures.
            }
        }

        return new NativePtyConnection(pty.ReaderStream, pty.WriterStream, WaitForExit, Dispose);
    }

    // Attempt to disable PTY slave ECHO so that input bytes forwarded from the outer
    // terminal (e.g. OSC color-query responses) are not echoed back into the recording
    // with ECHOCTL caret-notation conversion (ESC → "^[").
    private static void TryDisablePtyEcho(Stream masterStream)
    {
        if (
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)
        )
        {
            return;
        }

        try
        {
            // Quick.PtyNet wraps the master fd in a FileStream on Unix.
            if (masterStream is not FileStream fs)
            {
                return;
            }

            var fd = fs.SafeFileHandle;
            if (fd.IsInvalid)
            {
                return;
            }

            if (tcgetattr(fd, out var t) != 0)
            {
                return;
            }

            // Clear echo-related flags on the PTY slave (tcsetattr on the master fd
            // modifies the slave's termios settings on Linux/macOS).
            const uint ECHO = 0x0008u;
            const uint ECHOE = 0x0010u;
            const uint ECHOK = 0x0020u;
            const uint ECHONL = 0x0040u;
            const uint ECHOCTL = 0x0200u;
            t.c_lflag &= ~(ECHO | ECHOE | ECHOK | ECHONL | ECHOCTL);
            tcsetattr(fd, 0 /* TCSANOW */, ref t);
        }
        catch
        {
            // Best-effort; ignore failures.
        }
    }

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
    private static extern int tcgetattr(SafeFileHandle fd, out Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(
        SafeFileHandle fd,
        int optional_actions,
        ref Termios termios
    );
}
