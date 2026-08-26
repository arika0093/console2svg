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
    private static bool IsExpectedPtyEof(IOException exception)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        return exception.Message.Contains("Input/output error", StringComparison.OrdinalIgnoreCase);
    }

    public static IDisposable? TryUseUtf8ConsoleOutputEncoding(
        bool forwardToConsole,
        ILogger logger
    )
    {
        if (
            !forwardToConsole
            || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || Console.IsOutputRedirected
        )
        {
            return null;
        }

        try
        {
            var original = Console.OutputEncoding;
            if (original.CodePage == Encoding.UTF8.CodePage)
            {
                return null;
            }

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = utf8;
            logger.ZLogDebug(
                $"Temporarily set console output encoding to UTF-8 for forwarding. PreviousCodePage={original.CodePage}"
            );
            return new ConsoleOutputEncodingScope(original, logger);
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(
                ex,
                $"Failed to set console output encoding to UTF-8. Forwarding will use the current code page."
            );
            return null;
        }
    }

    // The cmd.exe fallback writes redirected stdout using the active Windows console code page.
    private static Encoding GetFallbackProcessOutputEncoding(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Console.OutputEncoding;
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(
                ex,
                $"Console output encoding is unavailable. Falling back to UTF-8 for process output decoding."
            );
            return Encoding.UTF8;
        }
    }

    private static async Task ReadOutputAsync(
        Stream readerStream,
        RecordingSession session,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        ILogger logger,
        Stream? forwardOutput,
        TextWriter? forwardOutputWriter,
        Encoding outputEncoding,
        double? outputCoalesceMs,
        double videoFps
    )
    {
        var bytes = new byte[4096];
        var chars = new char[8192];
        var decoder = outputEncoding.GetDecoder();
        var (coalesceWindowSeconds, maxBatchSeconds) = ResolveOutputCoalescing(
            outputCoalesceMs,
            videoFps
        );
        var pendingText = coalesceWindowSeconds > 0d ? new StringBuilder(4096) : null;
        var pendingFirstTime = 0d;
        var pendingLastTime = 0d;

        void FlushPending()
        {
            if (pendingText is null || pendingText.Length == 0)
            {
                return;
            }

            var text = pendingText.ToString();
            pendingText.Clear();
            session.AddEvent(pendingLastTime, text);
            logger.ZLogDebug(
                $"Captured coalesced chars={text.Length} elapsedMs={(long)(pendingLastTime * 1000)} preview={ToPreview(text)}"
            );
        }

        void AppendCapturedChars(
            char[] textBuffer,
            int charCount,
            double elapsedSeconds,
            int byteCount
        )
        {
            if (pendingText is null)
            {
                var text = new string(textBuffer, 0, charCount);
                session.AddEvent(elapsedSeconds, text);
                logger.ZLogDebug(
                    $"Captured chunk bytes={byteCount} chars={text.Length} elapsedMs={stopwatch.ElapsedMilliseconds} preview={ToPreview(text)}"
                );
                return;
            }

            if (pendingText.Length == 0)
            {
                pendingFirstTime = elapsedSeconds;
                pendingLastTime = elapsedSeconds;
                pendingText.Append(textBuffer, 0, charCount);
                return;
            }

            var gap = elapsedSeconds - pendingLastTime;
            if (
                gap > coalesceWindowSeconds
                || elapsedSeconds - pendingFirstTime >= maxBatchSeconds
            )
            {
                FlushPending();
                pendingFirstTime = elapsedSeconds;
            }

            pendingLastTime = elapsedSeconds;
            pendingText.Append(textBuffer, 0, charCount);
        }

        try
        {
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

                var charCount = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
                if (forwardOutput is not null)
                {
                    // Keep forwarded output byte-exact so VT/ANSI escape sequences are not altered.
                    try
                    {
                        await WriteForwardOutputAsync(
                                forwardOutput,
                                bytes,
                                count,
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore forwarding write failures; recording should continue.
                    }
                }

                if (forwardOutputWriter is not null && charCount > 0)
                {
                    try
                    {
                        await forwardOutputWriter
                            .WriteAsync(chars.AsMemory(0, charCount), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore forwarding write failures; recording should continue.
                    }
                }

                if (charCount <= 0)
                {
                    logger.ZLogDebug($"Read chunk bytes={count}, no chars decoded yet.");
                    continue;
                }

                AppendCapturedChars(chars, charCount, stopwatch.Elapsed.TotalSeconds, count);
            }
        }
        catch (OperationCanceledException)
        {
            FlushPending();
            throw;
        }

        var trailingCount = decoder.GetChars([], 0, 0, chars, 0, flush: true);
        if (trailingCount > 0)
        {
            if (forwardOutputWriter is not null)
            {
                try
                {
                    await forwardOutputWriter
                        .WriteAsync(chars.AsMemory(0, trailingCount), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Ignore forwarding write failures; recording should continue.
                }
            }

            AppendCapturedChars(
                chars,
                trailingCount,
                stopwatch.Elapsed.TotalSeconds,
                trailingCount
            );
        }

        FlushPending();
    }

    private static (double WindowSeconds, double MaxBatchSeconds) ResolveOutputCoalescing(
        double? outputCoalesceMs,
        double videoFps
    )
    {
        if (outputCoalesceMs.HasValue)
        {
            return outputCoalesceMs.Value > 0d
                ? (outputCoalesceMs.Value / 1000d, double.PositiveInfinity)
                : (0d, 0d);
        }

        var effectiveFps =
            videoFps > 0d && !double.IsNaN(videoFps) && !double.IsInfinity(videoFps)
                ? videoFps
                : 12d;
        var frameIntervalSeconds = 1d / effectiveFps;
        var windowSeconds = Math.Clamp(frameIntervalSeconds / 4d, 0.002d, 0.020d);
        return (windowSeconds, frameIntervalSeconds);
    }

    private static async Task WriteForwardOutputAsync(
        Stream forwardOutput,
        byte[] bytes,
        int byteCount,
        CancellationToken cancellationToken
    )
    {
        if (byteCount <= 0)
        {
            return;
        }

        await forwardOutput
            .WriteAsync(bytes, 0, byteCount, cancellationToken)
            .ConfigureAwait(false);
        await forwardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ConsoleOutputEncodingScope : IDisposable
    {
        private readonly Encoding _originalEncoding;
        private readonly ILogger _logger;
        private bool _disposed;

        public ConsoleOutputEncodingScope(Encoding originalEncoding, ILogger logger)
        {
            _originalEncoding = originalEncoding;
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Console.OutputEncoding = _originalEncoding;
                _logger.ZLogDebug(
                    $"Restored console output encoding. CodePage={_originalEncoding.CodePage}"
                );
            }
            catch (Exception ex)
            {
                _logger.ZLogDebug(ex, $"Failed to restore console output encoding.");
            }
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
}
