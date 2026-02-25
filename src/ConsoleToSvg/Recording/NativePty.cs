using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

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

        return Task.FromResult(NativePtyUnix.Spawn(options));
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
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    public static NativePtyConnection Spawn(NativePtyOptions options)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("ConPTY is only available on Windows.");
        }

        var size = new COORD((short)options.Cols, (short)options.Rows);
        CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0);
        CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0);

        SetHandleInformation(inputWrite, HANDLE_FLAG_INHERIT, 0);
        SetHandleInformation(outputRead, HANDLE_FLAG_INHERIT, 0);

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
            var startupInfo = new STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

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
            if (
                !CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    EXTENDED_STARTUPINFO_PRESENT,
                    environmentBlock,
                    options.Cwd,
                    ref startupInfo,
                    out var processInfo
                )
            )
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var reader = new FileStream(
                new SafeFileHandle(outputRead, ownsHandle: true),
                FileAccess.Read,
                4096,
                isAsync: true
            );
            var writer = new FileStream(
                new SafeFileHandle(inputWrite, ownsHandle: true),
                FileAccess.Write,
                4096,
                isAsync: true
            );

            var exited = false;

            bool WaitForExit(int milliseconds)
            {
                if (exited)
                {
                    return true;
                }

                var result = WaitForSingleObject(processInfo.hProcess, milliseconds);
                if (result == 0)
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
                }

                try
                {
                    writer.Dispose();
                }
                catch
                {
                }

                if (!exited)
                {
                    try
                    {
                        TerminateProcess(processInfo.hProcess, 1);
                    }
                    catch
                    {
                    }
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
        foreach (var pair in env)
        {
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
            builder.Append('\0');
        }

        builder.Append('\0');
        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private static void ThrowWin32(string message, int hr)
    {
        throw new Win32Exception(hr, message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
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
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
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
    private static extern bool SetHandleInformation(
        IntPtr hObject,
        uint dwMask,
        uint dwFlags
    );

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
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation
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
    private const int WNOHANG = 1;
    private const int SIGTERM = 15;

    public static NativePtyConnection Spawn(NativePtyOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("forkpty is not available on Windows.");
        }

        var size = new Winsize
        {
            ws_col = (ushort)options.Cols,
            ws_row = (ushort)options.Rows,
            ws_xpixel = 0,
            ws_ypixel = 0
        };

        var pid = forkpty(out var masterFd, IntPtr.Zero, IntPtr.Zero, ref size);
        if (pid < 0)
        {
            throw new InvalidOperationException($"forkpty failed. errno={Marshal.GetLastWin32Error()}");
        }

        if (pid == 0)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(options.Cwd))
                {
                    chdir(options.Cwd!);
                }

                if (options.Environment is not null)
                {
                    foreach (var pair in options.Environment)
                    {
                        setenv(pair.Key, pair.Value, 1);
                    }
                }

                var args = BuildArgv(options.App, options.Args);
                execvp(options.App, args);
            }
            catch
            {
            }

            _exit(127);
            return new NativePtyConnection(Stream.Null, Stream.Null, _ => true, () => { });
        }

        var readerHandle = new SafeFileHandle(new IntPtr(masterFd), ownsHandle: true);
        var writerFd = dup(masterFd);
        if (writerFd < 0)
        {
            readerHandle.Dispose();
            throw new InvalidOperationException($"dup failed. errno={Marshal.GetLastWin32Error()}");
        }

        var writerHandle = new SafeFileHandle(new IntPtr(writerFd), ownsHandle: true);
        var reader = new FileStream(readerHandle, FileAccess.Read, 4096, isAsync: true);
        var writer = new FileStream(writerHandle, FileAccess.Write, 4096, isAsync: true);

        var exited = false;

        bool WaitForExit(int milliseconds)
        {
            if (exited)
            {
                return true;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow <= deadline)
            {
                var result = waitpid(pid, out _, WNOHANG);
                if (result == pid)
                {
                    exited = true;
                    return true;
                }

                if (milliseconds <= 0)
                {
                    break;
                }

                Thread.Sleep(10);
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
            }

            try
            {
                writer.Dispose();
            }
            catch
            {
            }

            if (!exited)
            {
                try
                {
                    kill(pid, SIGTERM);
                }
                catch
                {
                }
            }

            try
            {
                waitpid(pid, out _, WNOHANG);
            }
            catch
            {
            }
        }

        return new NativePtyConnection(reader, writer, WaitForExit, Dispose);
    }

    private static IntPtr BuildArgv(string app, string[]? args)
    {
        var list = new List<string>();
        list.Add(app);
        if (args is not null)
        {
            list.AddRange(args);
        }

        var size = (list.Count + 1) * IntPtr.Size;
        var ptr = Marshal.AllocHGlobal(size);
        for (var i = 0; i < list.Count; i++)
        {
            Marshal.WriteIntPtr(ptr, i * IntPtr.Size, Marshal.StringToHGlobalAnsi(list[i]));
        }

        Marshal.WriteIntPtr(ptr, list.Count * IntPtr.Size, IntPtr.Zero);
        return ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int forkpty(
        out int master,
        IntPtr name,
        IntPtr termp,
        ref Winsize winsize
    );

    [DllImport("libc", SetLastError = true)]
    private static extern int execvp(string file, IntPtr argv);

    [DllImport("libc", SetLastError = true)]
    private static extern int chdir(string path);

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc")]
    private static extern void _exit(int status);
}
