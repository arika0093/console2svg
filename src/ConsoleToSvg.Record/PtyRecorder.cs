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
    // This sequence disables various mouse tracking modes in the terminal, which can be left enabled by some applications and cause issues with input forwarding (e.g. mouse clicks not working in Vim). It's safe to send this on every recording stop, even if the child process has already exited or doesn't support these modes.
    private const string DisableMouseTrackingSequence =
        "\u001b[?9l\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1004l\u001b[?1005l\u001b[?1006l\u001b[?1015l\u001b[?1016l\u001b[?9001l";

    private const int PtyCleanupTimeoutMs = 1000;

    // remove some CI environments to avoid apps switching to no-color mode.
    // for example: chalk(Node.js) checks "CI" to disable colors on CI environments:
    // see: https://github.com/chalk/chalk/blob/aa06bb5ac3f14df9fda8cfb54274dfc165ddfdef/source/vendor/supports-color/index.js#L114
    private static readonly string[] ShellDeletedEnvironmentKeys = ["CI", "TF_BUILD"];

    public static async Task<RecordingSession> RecordAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        bool forwardToConsole = true,
        bool noDeleteEnvs = false,
        string? replaySavePath = null,
        string? replayPath = null,
        double? outputCoalesceMs = null,
        double videoFps = 12d
    )
    {
        logger ??= NullLogger.Instance;
        logger.ZLogDebug($"Start PTY recording. Command={command} Width={width} Height={height}");

        const int MaxPtyStartupRetries = 3;
        const int PtyStartupTimeoutMs = 1000;

        for (var attempt = 1; attempt <= MaxPtyStartupRetries; attempt++)
        {
            try
            {
                return await RecordWithPtyAsync(
                        command,
                        width,
                        height,
                        cancellationToken,
                        logger,
                        forwardToConsole,
                        noDeleteEnvs,
                        replaySavePath,
                        replayPath,
                        outputCoalesceMs,
                        videoFps,
                        startupTimeoutMs: PtyStartupTimeoutMs
                    )
                    .ConfigureAwait(false);
            }
            catch (PtyStartupHangException ex)
            {
                if (attempt < MaxPtyStartupRetries)
                {
                    logger.ZLogDebug(
                        $"PTY startup hang detected (attempt {attempt}/{MaxPtyStartupRetries}). Retrying in 500ms..."
                    );
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                logger.ZLogDebug(
                    ex,
                    $"PTY startup hang after {attempt} attempt(s). Falling back to process execution."
                );
                return await RecordWithProcessFallbackAsync(
                        command,
                        width,
                        height,
                        cancellationToken,
                        logger,
                        forwardToConsole,
                        noDeleteEnvs,
                        replaySavePath,
                        replayPath,
                        outputCoalesceMs,
                        videoFps
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
                logger.ZLogDebug(
                    ex,
                    $"PTY backend unavailable. Falling back to process execution."
                );
                return await RecordWithProcessFallbackAsync(
                        command,
                        width,
                        height,
                        cancellationToken,
                        logger,
                        forwardToConsole,
                        noDeleteEnvs,
                        replaySavePath,
                        replayPath,
                        outputCoalesceMs,
                        videoFps
                    )
                    .ConfigureAwait(false);
            }
        }

        // Unreachable: the loop always exits via return or continue before exceeding MaxPtyStartupRetries.
        throw new InvalidOperationException("Unreachable code path in RecordAsync.");
    }

    private static async Task<RecordingSession> RecordWithPtyAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger,
        bool forwardToConsole,
        bool noDeleteEnvs,
        string? replaySavePath,
        string? replayPath,
        double? outputCoalesceMs,
        double videoFps,
        int? startupTimeoutMs = null
    )
    {
        var disableInputEcho = forwardToConsole && string.IsNullOrWhiteSpace(replayPath);
        var options = BuildOptions(logger, command, width, height, disableInputEcho, noDeleteEnvs);
        logger.ZLogDebug(
            $"Spawning PTY process. App={options.App} Args={string.Join(' ', options.Args ?? [])} Cwd={options.Cwd} Cols={options.Cols} Rows={options.Rows}"
        );
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();
        var canceled = false;
        var startupHang = false;
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
        using var utf8OutputScope = TryUseUtf8ConsoleOutputEncoding(forwardToConsole, logger);

        try
        {
            var connection = await NativePty
                .SpawnAsync(options, cancellationToken)
                .ConfigureAwait(false);
            logger.ZLogDebug($"PTY process spawned.");
            var outputForwardWriter = forwardToConsole ? TryOpenStandardOutputWriter(logger) : null;
            var outputForward =
                outputForwardWriter is null && forwardToConsole
                    ? TryOpenStandardOutput(logger)
                    : null;
            Stream? inputForward;
            InputReplayData? replayData = null;
            if (!string.IsNullOrWhiteSpace(replayPath))
            {
                logger.ZLogDebug($"Input source: replay file. Path={replayPath}");
                replayData = await InputReplayFile
                    .ReadDataAsync(replayPath, cancellationToken)
                    .ConfigureAwait(false);
                inputForward = new InputReplayFile.ReplayStream(replayData.Replay, logger);
            }
            else
            {
                inputForward = forwardToConsole ? TryOpenInputForForwarding(logger) : null;
            }

            InputReplayFile.InputReplayWriter? replaySaveWriter = null;
            if (!string.IsNullOrWhiteSpace(replaySavePath))
            {
                logger.ZLogDebug($"Saving input to replay file. Path={replaySavePath}");
                var dir = Path.GetDirectoryName(Path.GetFullPath(replaySavePath));
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                replaySaveWriter = new InputReplayFile.InputReplayWriter(
                    new FileStream(
                        replaySavePath,
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
                outputForward,
                outputForwardWriter,
                Encoding.UTF8,
                outputCoalesceMs,
                videoFps
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

                    if (
                        startupTimeoutMs.HasValue
                        && session.GetEventCount() == 0
                        && stopwatch.ElapsedMilliseconds > startupTimeoutMs.Value
                    )
                    {
                        startupHang = true;
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

            // When the process has exited but the read task has not completed,
            // give it time to drain any remaining buffered PTY output before
            // disposing the connection (which closes the reader stream).
            // 500ms is long enough for the kernel PTY buffer to flush in
            // practice but short enough to avoid noticeable UI lag.
            const int DrainTimeoutMs = 500;
            if (processExited && !eofReached && !canceled)
            {
                logger.ZLogDebug($"PTY process exited. Draining remaining output...");
                try
                {
                    var completed = await Task.WhenAny(
                            readTask,
                            Task.Delay(DrainTimeoutMs, cancellationToken)
                        )
                        .ConfigureAwait(false);
                    if (completed == readTask)
                    {
                        eofReached = true;
                        logger.ZLogDebug($"Read task completed during drain window.");
                    }
                    else
                    {
                        logger.ZLogDebug($"Read task did not complete within drain window.");
                    }
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
            }

            if (canceled)
            {
                await readCancellation.CancelAsync().ConfigureAwait(false);
            }

            await inputCancellation.CancelAsync().ConfigureAwait(false);

            if (canceled || eofReached || processExited)
            {
                string msg;
                if (eofReached)
                {
                    msg = "PTY output stream ended. Finalizing recording.";
                }
                else if (startupHang)
                {
                    msg =
                        $"PTY startup timeout ({startupTimeoutMs!.Value}ms): no output received. Finalizing recording.";
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
                disposed = true;
                await DisposeConnectionWithTimeoutAsync(connection, logger).ConfigureAwait(false);
            }

            var readerCompleted = await FinishReadTaskAsync(readTask, logger).ConfigureAwait(false);

            if (!canceled && !eofReached && !processExited && !disposed)
            {
                await DisposeConnectionWithTimeoutAsync(connection, logger).ConfigureAwait(false);
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
                $"PTY recording completed. Events={session.GetEventCount()} ElapsedMs={stopwatch.ElapsedMilliseconds}"
            );

            if (replayTimeoutExceeded is double exceededDurationFinal)
            {
                throw new TimeoutException(
                    $"Replay did not complete within the expected duration ({exceededDurationFinal:F1}s + 1s timeout)."
                );
            }

            if (startupHang)
            {
                throw new PtyStartupHangException(
                    $"PTY process did not produce any output within {startupTimeoutMs!.Value}ms of starting."
                );
            }

            if (!readerCompleted)
            {
                return SnapshotSession(session);
            }

            return session;
        }
        finally
        {
            TryDisableTerminalMouseTracking(forwardToConsole, logger);
        }
    }

    private static async Task DisposeConnectionWithTimeoutAsync(
        NativePtyConnection connection,
        ILogger logger
    )
    {
        var disposeTask = Task.Run(connection.Dispose);
        var completed = await Task.WhenAny(
                disposeTask,
                Task.Delay(PtyCleanupTimeoutMs, CancellationToken.None)
            )
            .ConfigureAwait(false);
        if (completed != disposeTask)
        {
            logger.ZLogDebug(
                $"PTY cleanup did not complete within {PtyCleanupTimeoutMs}ms; continuing shutdown."
            );
            return;
        }

        try
        {
            await disposeTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"PTY cleanup failed; continuing shutdown.");
        }
    }

    private static async Task<bool> FinishReadTaskAsync(Task readTask, ILogger logger)
    {
        var completed = await Task.WhenAny(
                readTask,
                Task.Delay(PtyCleanupTimeoutMs, CancellationToken.None)
            )
            .ConfigureAwait(false);
        if (completed != readTask)
        {
            logger.ZLogDebug(
                $"PTY output reader did not stop within {PtyCleanupTimeoutMs}ms; continuing shutdown."
            );
            return false;
        }

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

        return true;
    }

    private static RecordingSession SnapshotSession(RecordingSession source)
    {
        var snapshot = new RecordingSession(source.Header.width, source.Header.height)
        {
            Header =
            {
                timestamp = source.Header.timestamp,
            },
        };
        lock (source.EventsLock)
        {
            snapshot.Events.AddRange(source.Events);
        }
        return snapshot;
    }

    private static async Task<RecordingSession> RecordWithProcessFallbackAsync(
        string command,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger logger,
        bool forwardToConsole,
        bool noDeleteEnvs,
        string? replaySavePath,
        string? replayPath,
        double? outputCoalesceMs,
        double videoFps
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
        using var utf8OutputScope = TryUseUtf8ConsoleOutputEncoding(forwardToConsole, logger);

        var startInfo = BuildFallbackProcessStartInfo(command, noDeleteEnvs);
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

        try
        {
            var outputForwardWriter = forwardToConsole ? TryOpenStandardOutputWriter(logger) : null;
            var outputForward =
                outputForwardWriter is null && forwardToConsole
                    ? TryOpenStandardOutput(logger)
                    : null;
            Stream? inputForward;
            InputReplayData? replayData = null;
            if (!string.IsNullOrWhiteSpace(replayPath))
            {
                logger.ZLogDebug($"Input source: replay file. Path={replayPath}");
                replayData = await InputReplayFile
                    .ReadDataAsync(replayPath, cancellationToken)
                    .ConfigureAwait(false);
                inputForward = new InputReplayFile.ReplayStream(replayData.Replay, logger);
            }
            else
            {
                inputForward = forwardToConsole ? TryOpenInputForForwarding(logger) : null;
            }

            InputReplayFile.InputReplayWriter? replaySaveWriter = null;
            if (!string.IsNullOrWhiteSpace(replaySavePath))
            {
                logger.ZLogDebug($"Saving input to replay file. Path={replaySavePath}");
                var dir = Path.GetDirectoryName(Path.GetFullPath(replaySavePath));
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                replaySaveWriter = new InputReplayFile.InputReplayWriter(
                    new FileStream(
                        replaySavePath,
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
            var outputEncoding = GetFallbackProcessOutputEncoding(logger);
            try
            {
                await ReadOutputAsync(
                        process.StandardOutput.BaseStream,
                        session,
                        stopwatch,
                        replayTimeoutCts?.Token ?? CancellationToken.None,
                        logger,
                        outputForward,
                        outputForwardWriter,
                        outputEncoding,
                        outputCoalesceMs,
                        videoFps
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
                when (replayTimeoutCts is not null && ex.CancellationToken == replayTimeoutCts.Token
                )
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
                $"Fallback recording completed. ExitCode={process.ExitCode} Events={session.GetEventCount()} ElapsedMs={stopwatch.ElapsedMilliseconds} Canceled={canceled}"
            );

            if (replayTimedOut && replayData?.TotalDuration is double exceededDuration)
            {
                throw new TimeoutException(
                    $"Replay did not complete within the expected duration ({exceededDuration:F1}s + 1s timeout)."
                );
            }

            return session;
        }
        finally
        {
            TryDisableTerminalMouseTracking(forwardToConsole, logger);
        }
    }
}

// Exception type for PTY startup hang detection.
public sealed class PtyStartupHangException(string message) : Exception(message) { }
