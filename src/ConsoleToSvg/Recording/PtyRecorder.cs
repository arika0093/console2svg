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
using ZLogger;

namespace ConsoleToSvg.Recording;

public static class PtyRecorder
{
    public static async Task<RecordingSession> RecordAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        bool forwardToConsole = true,
        string? replaySavePath = null,
        string? replayPath = null
    )
    {
        logger ??= NullLogger.Instance;
        logger.ZLogDebug($"Start PTY recording. Command={command} Width={width} Height={height}");
        try
        {
            return await RecordWithPtyAsync(
                    command,
                    width,
                    height,
                    cancellationToken,
                    logger,
                    forwardToConsole,
                    replaySavePath,
                    replayPath
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is DllNotFoundException
                || ex is TypeInitializationException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException
            )
        {
            logger.ZLogDebug(ex, $"PTY backend unavailable. Falling back to process execution.");
            return await RecordWithProcessFallbackAsync(
                    command,
                    width,
                    height,
                    cancellationToken,
                    logger,
                    forwardToConsole,
                    replaySavePath,
                    replayPath
                )
                .ConfigureAwait(false);
        }
    }

    private static async Task<RecordingSession> RecordWithPtyAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger,
        bool forwardToConsole,
        string? replaySavePath,
        string? replayPath
    )
    {
        var disableInputEcho = forwardToConsole && string.IsNullOrWhiteSpace(replayPath);
        var options = BuildOptions(command, width, height, disableInputEcho);
        logger.ZLogDebug(
            $"Spawning PTY process. App={options.App} Args={string.Join(' ', options.Args ?? [])} Cwd={options.Cwd} Cols={options.Cols} Rows={options.Rows}"
        );
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();
        var canceled = false;
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        using var rawInput =
            forwardToConsole && string.IsNullOrWhiteSpace(replayPath)
                ? ConsoleInputMode.TryEnableRaw(logger)
                : null;

        var connection = await NativePty
            .SpawnAsync(options, cancellationToken)
            .ConfigureAwait(false);
        logger.ZLogDebug($"PTY process spawned.");
        var outputForward = forwardToConsole ? TryOpenStandardOutput(logger) : null;
        Stream? inputForward;
        InputReplayData? replayData = null;
        if (!string.IsNullOrWhiteSpace(replayPath))
        {
            logger.ZLogDebug($"Input source: replay file. Path={replayPath}");
            replayData = await InputReplayFile
                .ReadDataAsync(replayPath!, cancellationToken)
                .ConfigureAwait(false);
            inputForward = new InputReplayFile.ReplayStream(replayData.Replay);
        }
        else
        {
            inputForward = forwardToConsole ? TryOpenInputForForwarding(logger) : null;
        }

        InputReplayFile.InputReplayWriter? replaySaveWriter = null;
        if (!string.IsNullOrWhiteSpace(replaySavePath))
        {
            logger.ZLogDebug($"Saving input to replay file. Path={replaySavePath}");
            var dir = Path.GetDirectoryName(Path.GetFullPath(replaySavePath!));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            replaySaveWriter = new InputReplayFile.InputReplayWriter(
                new FileStream(
                    replaySavePath!,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous
                )
            );
        }

        var readTask = ReadOutputAsync(
            connection.ReaderStream,
            session,
            stopwatch,
            readCancellation.Token,
            logger,
            outputForward
        );
        var inputTask = inputForward is not null
            ? PumpInputAsync(
                inputForward,
                connection.WriterStream,
                inputCancellation.Token,
                logger,
                stopwatch,
                replaySaveWriter
            )
            : null;

        var eofReached = false;
        var processExited = false;
        var disposed = false;
        double? replayTimeoutExceeded = null;
        try
        {
            while (true)
            {
                if (readTask.IsCompleted)
                {
                    eofReached = true;
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                if (connection.WaitForExit(50))
                {
                    processExited = true;
                    break;
                }

                if (
                    replayData?.TotalDuration is double replayTotalDuration
                    && stopwatch.Elapsed.TotalSeconds > replayTotalDuration + 1.0
                )
                {
                    replayTimeoutExceeded = replayTotalDuration;
                    canceled = true;
                    break;
                }
            }
        }
        catch
        {
            // PTY process may have already exited; ignore cleanup errors such as
            // "Killing terminal failed with error 3" (ESRCH: no such process)
        }

        if (replayTimeoutExceeded is double exceededDuration)
        {
            logger.ZLogDebug(
                $"Replay timeout exceeded ({exceededDuration:F1}s + 1s). Finalizing PTY recording."
            );
        }

        if (canceled || eofReached || processExited)
        {
            string msg;
            if (eofReached)
            {
                msg = "PTY output stream ended. Finalizing recording.";
            }
            else if (canceled)
            {
                msg = "Cancellation requested. Finalizing partial PTY recording.";
            }
            else
            {
                msg = "PTY process exited. Finalizing recording.";
            }
            logger.ZLogDebug($"{msg}");
            try
            {
                connection.Dispose();
                disposed = true;
            }
            catch
            {
                // Ignore disposal errors during cancellation cleanup.
            }
        }

        if (canceled)
        {
            await readCancellation.CancelAsync().ConfigureAwait(false);
        }

        await inputCancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Read task cancellation is treated as graceful completion for partial output.
        }
        catch (IOException ex) when (IsExpectedPtyEof(ex))
        {
            // On Unix PTY, child exit can surface as EIO ("Input/output error")
            // when reading after the slave side is closed. Treat as EOF.
        }

        if (!canceled && !eofReached && !processExited && !disposed)
        {
            try
            {
                connection.Dispose();
            }
            catch
            {
                // Ignore disposal errors when the process has already exited
            }
        }

        if (inputTask is not null)
        {
            await IgnoreTaskFailureWithTimeoutAsync(inputTask, 200).ConfigureAwait(false);
        }

        if (replaySaveWriter != null)
        {
            try
            {
                replaySaveWriter.TotalDuration = stopwatch.Elapsed.TotalSeconds;
                await replaySaveWriter.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        logger.ZLogDebug(
            $"PTY recording completed. Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}"
        );

        if (replayTimeoutExceeded is double exceededDurationFinal)
        {
            throw new TimeoutException(
                $"Replay did not complete within the expected duration ({exceededDurationFinal:F1}s + 1s timeout)."
            );
        }

        return session;
    }

    private static async Task<RecordingSession> RecordWithProcessFallbackAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger,
        bool forwardToConsole,
        string? replaySavePath,
        string? replayPath
    )
    {
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();
        var canceled = false;
        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        using var rawInput =
            forwardToConsole && string.IsNullOrWhiteSpace(replayPath)
                ? ConsoleInputMode.TryEnableRaw(logger)
                : null;

        var startInfo = BuildFallbackProcessStartInfo(command);
        logger.ZLogDebug(
            $"Using process fallback. FileName={startInfo.FileName} Arguments={startInfo.Arguments} Cwd={startInfo.WorkingDirectory}"
        );
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.Start();
        logger.ZLogDebug($"Fallback process started. Pid={process.Id}");

        using var registration = cancellationToken.Register(() =>
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
        });

        var outputForward = forwardToConsole ? TryOpenStandardOutput(logger) : null;
        Stream? inputForward;
        InputReplayData? replayData = null;
        if (!string.IsNullOrWhiteSpace(replayPath))
        {
            logger.ZLogDebug($"Input source: replay file. Path={replayPath}");
            replayData = await InputReplayFile
                .ReadDataAsync(replayPath!, cancellationToken)
                .ConfigureAwait(false);
            inputForward = new InputReplayFile.ReplayStream(replayData.Replay);
        }
        else
        {
            inputForward = forwardToConsole ? TryOpenInputForForwarding(logger) : null;
        }

        InputReplayFile.InputReplayWriter? replaySaveWriter = null;
        if (!string.IsNullOrWhiteSpace(replaySavePath))
        {
            logger.ZLogDebug($"Saving input to replay file. Path={replaySavePath}");
            var dir = Path.GetDirectoryName(Path.GetFullPath(replaySavePath!));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            replaySaveWriter = new InputReplayFile.InputReplayWriter(
                new FileStream(
                    replaySavePath!,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous
                )
            );
        }

        var inputTask = inputForward is not null
            ? PumpInputAsync(
                inputForward,
                process.StandardInput.BaseStream,
                inputCancellation.Token,
                logger,
                stopwatch,
                replaySaveWriter
            )
            : null;

        // When replaying with a TotalDuration, create a timeout CTS so that ReadOutputAsync
        // is cancelled (and the process is killed) if stdout stays open beyond the deadline.
        using var replayTimeoutCts = replayData?.TotalDuration is double replayTotalDur
            ? new CancellationTokenSource(TimeSpan.FromSeconds(replayTotalDur + 1.0))
            : null;
        using var timeoutKillRegistration = replayTimeoutCts?.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process has already exited; nothing to kill.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Kill may fail on Windows if the process is already gone.
            }
        });

        var replayTimedOut = false;
        try
        {
            await ReadOutputAsync(
                    process.StandardOutput.BaseStream,
                    session,
                    stopwatch,
                    replayTimeoutCts?.Token ?? CancellationToken.None,
                    logger,
                    outputForward
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (replayTimeoutCts is not null && ex.CancellationToken == replayTimeoutCts.Token)
        {
            replayTimedOut = true;
        }

        while (!process.WaitForExit(50))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }
        }

        await inputCancellation.CancelAsync().ConfigureAwait(false);
        if (inputTask is not null)
        {
            await IgnoreTaskFailureWithTimeoutAsync(inputTask, 200).ConfigureAwait(false);
        }

        if (replaySaveWriter != null)
        {
            try
            {
                replaySaveWriter.TotalDuration = stopwatch.Elapsed.TotalSeconds;
                await replaySaveWriter.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        logger.ZLogDebug(
            $"Fallback recording completed. ExitCode={process.ExitCode} Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds} Canceled={canceled}"
        );

        if (replayTimedOut && replayData?.TotalDuration is double exceededDuration)
        {
            throw new TimeoutException(
                $"Replay did not complete within the expected duration ({exceededDuration:F1}s + 1s timeout)."
            );
        }

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
        ILogger logger,
        Stream? forwardOutput
    )
    {
        var bytes = new byte[4096];
        var chars = new char[8192];
        var decoder = Encoding.UTF8.GetDecoder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count;
            try
            {
                count = await readerStream
                    .ReadAsync(bytes, 0, bytes.Length, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                logger.ZLogDebug($"Read stream disposed; treating as EOF.");
                break;
            }
            if (count <= 0)
            {
                logger.ZLogDebug($"Read stream completed (EOF).");
                break;
            }

            if (forwardOutput is not null)
            {
                try
                {
                    await forwardOutput
                        .WriteAsync(bytes, 0, count, cancellationToken)
                        .ConfigureAwait(false);
                    await forwardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore forwarding write failures; recording should continue.
                }
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
            logger.ZLogDebug(
                $"Captured trailing decoded chars={trailingCount} preview={ToPreview(trailing)}"
            );
        }
    }

    private static async Task PumpInputAsync(
        Stream sourceInput,
        Stream targetInput,
        CancellationToken cancellationToken,
        ILogger logger,
        Stopwatch? stopwatch = null,
        InputReplayFile.InputReplayWriter? inputSave = null
    )
    {
        var buffer = new byte[256];
        // Use UTF-8 for decoding VT input.  VT sequences are always ASCII, and
        // Console.InputEncoding (e.g. CP932 / Shift_JIS on Japanese Windows)
        // may treat ESC (0x1B) as an ISO-2022 lead byte and silently consume it,
        // which breaks every CSI sequence (arrows, Shift+Tab, etc.).
        // Modern Windows Terminal sends all characters (including CJK) as UTF-8
        // regardless of the console code page, so UTF-8 is the correct choice.
        var inputDecoder = Encoding.UTF8.GetDecoder();
        var inputChars = new char[512];
        // Carry-over for incomplete ESC sequences split across reads.
        var pending = "";
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await sourceInput
                    .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);
                if (count <= 0)
                {
                    break;
                }

                await targetInput
                    .WriteAsync(buffer, 0, count, cancellationToken)
                    .ConfigureAwait(false);
                await targetInput.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (inputSave != null && stopwatch != null)
                {
                    var charCount = inputDecoder.GetChars(
                        buffer,
                        0,
                        count,
                        inputChars,
                        0,
                        flush: false
                    );
                    if (charCount > 0)
                    {
                        var text = pending + new string(inputChars, 0, charCount);
                        var t = stopwatch.Elapsed.TotalSeconds;
                        var (events, remainder) = InputReplayFile.ParseInputTextPartial(text, t);
                        foreach (var evt in events)
                            inputSave.AppendEvent(evt);
                        pending = remainder;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when recording stops.
        }
        catch (IOException)
        {
            // Child process may exit while forwarding input.
        }
        catch (ObjectDisposedException)
        {
            // Child process input stream already disposed.
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Input forwarding failed.");
        }
        finally
        {
            // Flush any remaining incomplete sequence (treat as-is).
            // This must be in finally because OperationCanceledException from
            // ReadAsync would otherwise skip the flush, losing the last pending event.
            if (inputSave != null && stopwatch != null && pending.Length > 0)
            {
                var t = stopwatch?.Elapsed.TotalSeconds ?? 0;
                foreach (var evt in InputReplayFile.ParseInputText(pending, t))
                    inputSave.AppendEvent(evt);
            }
        }
    }

    private static async Task IgnoreTaskFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Ignore background task failures during shutdown.
        }
    }

    private static async Task IgnoreTaskFailureWithTimeoutAsync(Task task, int milliseconds)
    {
        var completed = await Task.WhenAny(task, Task.Delay(milliseconds)).ConfigureAwait(false);
        if (completed == task)
        {
            await IgnoreTaskFailureAsync(task).ConfigureAwait(false);
        }
    }

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

        var normalized = builder.ToString();
        return normalized.Length <= 120 ? normalized : normalized.Substring(0, 120) + "...";
    }

    private static NativePtyOptions BuildOptions(
        string command,
        int width,
        int height,
        bool disableInputEcho
    )
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

        // Ensure TERM is set to a value that supports color and cursor control.
        // On headless CI runners (ubuntu-latest, etc.), TERM is often unset or "dumb",
        // which prevents programs like vim from rendering characters in the PTY.
        env["TERM"] = "xterm-256color";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new NativePtyOptions
            {
                Name = "console2svg",
                Cols = width,
                Rows = height,
                Cwd = Environment.CurrentDirectory,
                App = "cmd.exe",
                Args = ["/d", "/c", command],
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
            Args = ["-lc", command],
            Environment = env,
            DisableInputEcho = disableInputEcho,
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
                "-lc \"" + command.Replace("\"", "\\\"", StringComparison.Ordinal) + " 2>&1\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
    }

    private sealed class ConsoleInputMode : IDisposable
    {
        private const uint StdInputHandle = 0xFFFFFFF6;
        private const uint EnableProcessedInput = 0x0001;
        private const uint EnableLineInput = 0x0002;
        private const uint EnableEchoInput = 0x0004;
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
                newMode |= EnableVirtualTerminalInput | EnableExtendedFlags;
                newMode &= ~(EnableLineInput | EnableEchoInput | EnableProcessedInput);
                newMode &= ~EnableQuickEditMode;

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
                    return null;
                }

                var raw = termios;
                raw.c_iflag &= ~(BRKINT | ICRNL | INPCK | ISTRIP | IXON);
                raw.c_oflag &= ~OPOST;
                raw.c_cflag |= CS8;
                raw.c_lflag &= ~(ICANON | ECHO | IEXTEN | ISIG);
                raw.c_cc[VMIN] = 1;
                raw.c_cc[VTIME] = 0;
                if (tcsetattr(stdinFd, TCSANOW, ref raw) != 0)
                {
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
        private const uint BRKINT = 0x0002;
        private const uint ICRNL = 0x0100;
        private const uint INPCK = 0x0010;
        private const uint ISTRIP = 0x0020;
        private const uint IXON = 0x0400;
        private const uint OPOST = 0x0001;
        private const uint CS8 = 0x0030;
        private const uint ICANON = 0x0002;
        private const uint ECHO = 0x0008;
        private const uint IEXTEN = 0x8000;
        private const uint ISIG = 0x0001;
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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(uint nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}
