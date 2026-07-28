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

public sealed class InteractiveCapture
{
    public InteractiveCapture(ScreenBuffer screen)
    {
        Screen = screen;
        Frames = [];
    }

    public InteractiveCapture(IReadOnlyList<TerminalFrame> frames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        Frames = frames;
        Screen = frames[0].Buffer;
    }

    public ScreenBuffer Screen { get; }

    public IReadOnlyList<TerminalFrame> Frames { get; }

    public bool IsVideo => Frames.Count > 0;
}

/// <summary>Runs an interactive shell in a PTY and emits snapshots without sending capture keys to it.</summary>
public static class InteractiveRecorder
{
    public static async Task RunAsync(
        int width,
        int height,
        Theme theme,
        ReadOnlyMemory<byte> screenshotKey,
        ReadOnlyMemory<byte> recordingKey,
        ReadOnlyMemory<byte> pauseKey,
        bool noDeleteEnvs,
        string[]? command,
        bool exitOnCtrlD,
        bool recordingEnabled,
        bool screenshotEnabled,
        Func<InteractiveCapture, Task<string?>> onCapture,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        if (screenshotKey.IsEmpty || recordingKey.IsEmpty || pauseKey.IsEmpty)
        {
            throw new ArgumentException("Screenshot, recording, and pause keys are required.");
        }

        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        var emulator = new TerminalEmulator(width, height, theme);
        var captureGate = new object();
        using var hostOutputGate = new SemaphoreSlim(1, 1);
        long notificationVersion = 0;
        var notificationColumn = 1;
        var notificationLength = 0;
        var recordingIndicatorActive = 0;
        var recordingPaused = 0;
        var startupIndicatorActive = 0;
        List<TerminalFrame>? videoFrames = null;
        var videoStarted = 0d;
        var videoPausedAt = 0d;
        var videoPausedDuration = 0d;
        var stopwatch = Stopwatch.StartNew();
        long lastOutputTimestamp = Stopwatch.GetTimestamp();
        var canRecord = recordingEnabled;
        var canScreenshot = screenshotEnabled;
        var notificationActive = 0;

        var options = BuildOptions(width, height, noDeleteEnvs, command);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = Console.OpenStandardOutput();
        var input = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Console.OpenStandardInput()
            : null;
        PtyRecorder.TryDisableTerminalMouseTracking(forwardToConsole: true, logger);

        async Task ClearHostTerminalAsync()
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            await hostOutputGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                // Match the visible part of `cls` without sending anything to the
                // child PTY. The child will still receive Ctrl+L and redraw itself.
                var clear = "\u001b[2J\u001b[H";
                await output.WriteAsync(Encoding.ASCII.GetBytes(clear), CancellationToken.None)
                    .ConfigureAwait(false);
                await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                hostOutputGate.Release();
            }
        }

        var connection = await NativePty
            .SpawnAsync(options, cancellationToken)
            .ConfigureAwait(false);
        var rawInput = PtyRecorder.ConsoleInputMode.TryEnableRaw(logger);
        using var utf8OutputScope = PtyRecorder.TryUseUtf8ConsoleOutputEncoding(
            forwardToConsole: true,
            logger
        );
        // Console.Out is recreated when the console encoding changes. Acquire it
        // after entering the UTF-8 scope so its writer matches the console.
        var outputWriter =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Console.IsOutputRedirected
                ? Console.Out
                : null;
        try
        {
            await ClearHostTerminalAsync().ConfigureAwait(false);
        }
        catch
        {
            rawInput?.Dispose();
            connection.Dispose();
            throw;
        }

        async Task NotifyAsync(string message, bool isRecording, bool isError, long version)
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            if (Volatile.Read(ref notificationVersion) != version)
            {
                return;
            }

            var consoleWidth = Math.Max(20, Console.WindowWidth);
            var label = $"  {message}  ";
            if (label.Length > consoleWidth - 2)
            {
                label = label[..Math.Max(1, consoleWidth - 2)];
            }

            var column = Math.Max(1, consoleWidth - label.Length);
            await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref notificationVersion) != version)
                {
                    return;
                }

                string style;
                if (isError)
                {
                    style = "\u001b[1;97;48;5;88m";  // White on dark red background for errors
                }
                else if (isRecording)
                {
                    style = "\u001b[1;31;48;5;236m";  // Red on dark gray for recording
                }
                else
                {
                    style = "\u001b[30;48;5;114m";   // Black on green for normal
                }
                var clearPrevious = notificationLength == 0
                    ? string.Empty
                    : $"\u001b[1;{notificationColumn}H{new string(' ', notificationLength)}";
                var overlay =
                    $"\u001b7{clearPrevious}\u001b[1;{column}H{style}{label}\u001b[0m\u001b8";
                await output
                    .WriteAsync(Encoding.UTF8.GetBytes(overlay), lifetime.Token)
                    .ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                notificationColumn = column;
                notificationLength = label.Length;
            }
            finally
            {
                hostOutputGate.Release();
            }

        }

        async Task ClearNotificationAsync(long version)
        {
            if (Volatile.Read(ref notificationVersion) != version)
            {
                return;
            }

            await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref notificationVersion) != version)
                {
                    return;
                }

                var clear =
                    $"\u001b7\u001b[1;{notificationColumn}H{new string(' ', notificationLength)}\u001b8";
                await output
                    .WriteAsync(Encoding.UTF8.GetBytes(clear), lifetime.Token)
                    .ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                notificationLength = 0;
            }
            finally
            {
                hostOutputGate.Release();
            }
        }

        async Task ShowTimedNotificationAsync(string message, bool isRecording, bool isError, long version)
        {
            await NotifyAsync(message, isRecording, isError, version).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), lifetime.Token).ConfigureAwait(false);
            await ClearNotificationAsync(version).ConfigureAwait(false);
            // Mark notification as no longer active before restoring persistent indicator
            Interlocked.Exchange(ref notificationActive, 0);
            // After timed notification clears, restore persistent indicator
            if (Volatile.Read(ref notificationVersion) == version)
            {
                await RenderPersistentIndicatorAsync().ConfigureAwait(false);
            }
        }

        (long Version, Task Completion) ShowNotification(string message, bool isRecording = false, bool isError = false)
        {
            var version = Interlocked.Increment(ref notificationVersion);
            Interlocked.Exchange(ref notificationActive, 1);
            var notification = ShowTimedNotificationAsync(message, isRecording, isError, version);
            _ = notification.ContinueWith(
                task => logger.ZLogDebug(task.Exception, $"Interactive notification failed."),
                TaskContinuationOptions.OnlyOnFaulted
            );
            return (version, notification);
        }

        void ShowPersistentNotification(string message)
        {
            var version = Interlocked.Increment(ref notificationVersion);
            Interlocked.Exchange(ref notificationActive, 1);
            var notification = NotifyAsync(message, isRecording: false, isError: false, version: version);
            _ = notification.ContinueWith(
                task => logger.ZLogDebug(task.Exception, $"Interactive notification failed."),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }

        async Task RenderPersistentIndicatorAsync()
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            // Skip if a timed notification is currently active
            if (Volatile.Read(ref notificationActive) != 0)
            {
                return;
            }

            var isRecording = Volatile.Read(ref recordingIndicatorActive) != 0;
            if (!isRecording && Volatile.Read(ref startupIndicatorActive) == 0)
            {
                return;
            }

            // Build label based on enabled features
            var hints = new List<string>();
            if (canRecord)
            {
                hints.Add("F9: Record start");
            }
            if (canScreenshot)
            {
                hints.Add("F10: Capture");
            }
            
            string label;
            if (isRecording)
            {
                label = Volatile.Read(ref recordingPaused) != 0
                    ? "  ● REC (F9:End, F12:Resume)  "
                    : "  ● REC (F9:End, F12:Pause)  ";
            }
            else if (hints.Count > 0)
            {
                label = "  " + string.Join("   ", hints) + "  ";
            }
            else
            {
                // No features enabled, don't show indicator
                return;
            }
            
            var width = Math.Max(20, Console.WindowWidth);
            var column = Math.Max(1, width - label.Length);
            var clearPrevious = notificationLength == 0
                ? string.Empty
                : $"\u001b[1;{notificationColumn}H{new string(' ', notificationLength)}";
            var overlay =
                isRecording
                    ? $"\u001b7{clearPrevious}\u001b[1;{column}H\u001b[1;31;48;5;236m{label}\u001b[0m\u001b8"
                    : $"\u001b7{clearPrevious}\u001b[1;{column}H\u001b[30;48;5;114m{label}\u001b[0m\u001b8";
            await output
                .WriteAsync(Encoding.UTF8.GetBytes(overlay), lifetime.Token)
                .ConfigureAwait(false);
            await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
            notificationColumn = column;
            notificationLength = label.Length;
        }

        void ShowRecordingStartedNotification()
        {
            Interlocked.Exchange(ref startupIndicatorActive, 0);
            ShowNotification("Started");
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await Task
                            .Delay(TimeSpan.FromMilliseconds(1500), lifetime.Token)
                            .ConfigureAwait(false);
                        lock (captureGate)
                        {
                            if (videoFrames is null)
                            {
                                return;
                            }
                        }

                        Interlocked.Exchange(ref recordingIndicatorActive, 1);
                        Interlocked.Increment(ref notificationVersion);
                        await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        try
                        {
                            await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            hostOutputGate.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // The session ended before the recording indicator was due.
                    }
                },
                CancellationToken.None
            ).ContinueWith(
                task => logger.ZLogDebug(task.Exception, $"Interactive notification failed."),
                TaskContinuationOptions.OnlyOnFaulted
            );
        }

        async Task WaitForOutputToSettleAsync()
        {
            const int quietMilliseconds = 100;
            const int maxWaitMilliseconds = 750;
            // Allow output caused by the immediately preceding Enter/key press to
            // reach the PTY before considering the terminal idle.
            await Task.Delay(150, lifetime.Token).ConfigureAwait(false);
            var started = Stopwatch.GetTimestamp();
            while (!lifetime.IsCancellationRequested)
            {
                var now = Stopwatch.GetTimestamp();
                var quietFor = (now - Volatile.Read(ref lastOutputTimestamp)) * 1000 / Stopwatch.Frequency;
                var waited = (now - started) * 1000 / Stopwatch.Frequency;
                if (quietFor >= quietMilliseconds || waited >= maxWaitMilliseconds)
                {
                    return;
                }

                await Task.Delay(20, lifetime.Token).ConfigureAwait(false);
            }
        }

        async Task SaveCaptureAsync(InteractiveCapture capture)
        {
            ShowPersistentNotification("Saving...");
            try
            {
                var message = await onCapture(capture).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    var savedNotification = ShowNotification(message);
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await savedNotification.Completion.ConfigureAwait(false);
                                if (
                                    Volatile.Read(ref notificationVersion) != savedNotification.Version
                                    || Volatile.Read(ref recordingIndicatorActive) != 0
                                )
                                {
                                    return;
                                }

                                lock (captureGate)
                                {
                                    if (videoFrames is not null)
                                    {
                                        return;
                                    }
                                }

                                Interlocked.Exchange(ref startupIndicatorActive, 1);
                                await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                try
                                {
                                    if (
                                        Volatile.Read(ref notificationVersion)
                                        == savedNotification.Version
                                    )
                                    {
                                        await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    hostOutputGate.Release();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // The session ended before restoring the startup indicator.
                            }
                        },
                        CancellationToken.None
                    ).ContinueWith(
                        task => logger.ZLogDebug(task.Exception, $"Interactive notification failed."),
                        TaskContinuationOptions.OnlyOnFaulted
                    );
                }
            }
            catch
            {
                ShowNotification("Save failed", isError: true);
                throw;
            }
        }

        async Task<InteractiveCapture> CaptureScreenshotAsync()
        {
            await WaitForOutputToSettleAsync().ConfigureAwait(false);
            InteractiveCapture capture;
            lock (captureGate)
            {
                capture = new InteractiveCapture(emulator.Buffer.Clone());
            }

            return capture;
        }

        async Task<InteractiveCapture?> ToggleRecordingAsync()
        {
            if (!recordingEnabled)
            {
                ShowNotification("Recording requires SVG or a video output format", isError: true);
                return null;
            }

            var isStarting = false;
            lock (captureGate)
            {
                if (videoFrames is null)
                {
                    videoStarted = stopwatch.Elapsed.TotalSeconds;
                    videoPausedDuration = 0d;
                    videoPausedAt = 0d;
                    videoFrames = [new TerminalFrame(0d, emulator.Buffer.Clone())];
                    isStarting = true;
                }
            }

            if (isStarting)
            {
                ShowRecordingStartedNotification();
                return null;
            }

            await WaitForOutputToSettleAsync().ConfigureAwait(false);
            InteractiveCapture capture;
            lock (captureGate)
            {
                var finalScreen = Volatile.Read(ref recordingPaused) != 0
                    ? videoFrames[^1].Buffer
                    : emulator.Buffer;
                capture = CompleteRecording(
                    videoFrames,
                    GetRecordingElapsedSeconds(),
                    finalScreen
                );
                videoFrames = null;
                videoPausedDuration = 0d;
                videoPausedAt = 0d;
            }
            Interlocked.Exchange(ref recordingIndicatorActive, 0);
            Interlocked.Exchange(ref recordingPaused, 0);

            return capture;
        }

        async Task TogglePauseAsync()
        {
            var changed = false;
            lock (captureGate)
            {
                if (videoFrames is null)
                {
                    return;
                }

                if (Volatile.Read(ref recordingPaused) == 0)
                {
                    videoPausedAt = stopwatch.Elapsed.TotalSeconds;
                    Interlocked.Exchange(ref recordingPaused, 1);
                }
                else
                {
                    videoPausedDuration += stopwatch.Elapsed.TotalSeconds - videoPausedAt;
                    videoPausedAt = 0d;
                    Interlocked.Exchange(ref recordingPaused, 0);
                }

                changed = true;
            }

            if (changed)
            {
                Interlocked.Exchange(ref recordingIndicatorActive, 1);
                Interlocked.Increment(ref notificationVersion);
                await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                }
                finally
                {
                    hostOutputGate.Release();
                }
            }
        }

        double GetRecordingElapsedSeconds()
        {
            var elapsed = stopwatch.Elapsed.TotalSeconds - videoStarted - videoPausedDuration;
            if (Volatile.Read(ref recordingPaused) != 0)
            {
                elapsed -= stopwatch.Elapsed.TotalSeconds - videoPausedAt;
            }

            return Math.Max(0d, elapsed);
        }

        var outputTask = Task.Run(
            async () =>
            {
                var bytes = new byte[4096];
                var chars = new char[8192];
                var decoder = Encoding.UTF8.GetDecoder();
                var hostSequenceFilter = new HostTerminalSequenceFilter();
                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        var count = await connection.ReaderStream
                            .ReadAsync(bytes, 0, bytes.Length, lifetime.Token)
                            .ConfigureAwait(false);
                        if (count <= 0)
                        {
                            break;
                        }

                        var charCount = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
                        lock (captureGate)
                        {
                            if (charCount > 0)
                            {
                                var text = new string(chars, 0, charCount);
                                emulator.Process(text);
                                Volatile.Write(ref lastOutputTimestamp, Stopwatch.GetTimestamp());
                                if (
                                    videoFrames is not null
                                    && Volatile.Read(ref recordingPaused) == 0
                                )
                                {
                                    videoFrames.Add(
                                        new TerminalFrame(
                                            GetRecordingElapsedSeconds(),
                                            emulator.Buffer.Clone()
                                        )
                                    );
                                }
                            }
                        }

                        if (outputWriter is not null)
                        {
                            if (charCount > 0)
                            {
                                var text = hostSequenceFilter.Filter(new string(chars, 0, charCount));
                                if (text.Length > 0)
                                {
                                    await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                    try
                                    {
                                        await outputWriter
                                            .WriteAsync(text.AsMemory(), lifetime.Token)
                                            .ConfigureAwait(false);
                                        await outputWriter.FlushAsync(lifetime.Token).ConfigureAwait(false);
                                        await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        hostOutputGate.Release();
                                    }
                                }
                            }
                        }
                        else
                        {
                            var text = charCount > 0
                                ? hostSequenceFilter.Filter(new string(chars, 0, charCount))
                                : string.Empty;
                            if (text.Length == 0)
                            {
                                continue;
                            }

                            await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                            try
                            {
                                await output
                                    .WriteAsync(
                                        Encoding.UTF8.GetBytes(text),
                                        lifetime.Token
                                    )
                                    .ConfigureAwait(false);
                                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                                await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                            }
                            finally
                            {
                                hostOutputGate.Release();
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown.
                }
                catch (IOException) when (lifetime.IsCancellationRequested)
                {
                    // Closing a Unix PTY may surface as EIO.
                }
            },
            CancellationToken.None
        );

        var captureQueueGate = new object();
        Task captureQueue = Task.CompletedTask;

        void QueueSaveCapture(InteractiveCapture capture)
        {
            lock (captureQueueGate)
            {
                captureQueue = captureQueue.ContinueWith(
                    async _ =>
                    {
                        try
                        {
                            await SaveCaptureAsync(capture).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            logger.ZLogError(ex, $"Interactive capture failed.");
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                ).Unwrap();
            }
        }

        var inputTask = Task.Run(
            async () =>
            {
                var bytes = new byte[256];
                var router = new InteractiveInputRouter(
                    screenshotKey.Span,
                    recordingKey.Span,
                    pauseKey.Span
                );
                var inputGate = new SemaphoreSlim(1, 1);
                long escapePendingVersion = 0;
                logger.ZLogDebug($"Interactive input forwarding started.");

                void ScheduleStandaloneEscape(long version)
                {
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                // Some WSL terminal stacks deliver a function-key
                                // sequence in separate reads. Keep ESC long enough
                                // for the remaining CSI bytes to arrive.
                                await Task.Delay(100, lifetime.Token).ConfigureAwait(false);
                                await inputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                                try
                                {
                                    if (
                                        Volatile.Read(ref escapePendingVersion) == version
                                        && router.HasStandaloneEscape
                                    )
                                    {
                                        var forwarded = new List<byte>(1);
                                        router.ForwardPending(forwarded);
                                        await connection.WriterStream
                                            .WriteAsync(forwarded.ToArray(), lifetime.Token)
                                            .ConfigureAwait(false);
                                        await connection.WriterStream
                                            .FlushAsync(lifetime.Token)
                                            .ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    inputGate.Release();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // The session ended before a standalone Escape was due.
                            }
                        },
                        CancellationToken.None
                    );
                }

                try
                {
                    while (!lifetime.IsCancellationRequested)
                    {
                        var count = input is not null
                            ? await input
                                .ReadAsync(bytes, 0, bytes.Length, lifetime.Token)
                                .ConfigureAwait(false)
                            : await Task.Run(
                                () => ReadUnixTerminalInput(bytes, timeoutMilliseconds: 100),
                                CancellationToken.None
                            ).ConfigureAwait(false);
                        if (count < 0)
                        {
                            // Poll timeout; check cancellation and continue.
                            continue;
                        }
                        if (count <= 0)
                        {
                            // An EOF from the outer terminal is another form of
                            // Ctrl+D. Do not leave the child shell running after its
                            // input owner has gone away.
                            await lifetime.CancelAsync().ConfigureAwait(false);
                            break;
                        }

                        await inputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        var captures = new List<InteractiveCapture>();
                        try
                        {
                            // A new byte invalidates any pending standalone-Escape timer.
                            Interlocked.Increment(ref escapePendingVersion);
                            for (var i = 0; i < count; i++)
                            {
                                var forwarded = new List<byte>();
                                var action = router.Process(bytes[i], forwarded);
                                if (forwarded.Count > 0)
                                {
                                    await connection.WriterStream
                                        .WriteAsync(forwarded.ToArray(), lifetime.Token)
                                        .ConfigureAwait(false);
                                }

                                switch (action)
                                {
                                    case InteractiveInputAction.Exit:
                                        if (
                                            exitOnCtrlD
                                            && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                        )
                                        {
                                            await lifetime.CancelAsync().ConfigureAwait(false);
                                            return;
                                        }

                                        // Bash receives the EOT byte above and exits;
                                        // wait for the PTY's normal process-exit path.
                                        break;
                                    case InteractiveInputAction.Screenshot:
                                        if (!screenshotEnabled)
                                        {
                                            ShowNotification("Screenshots are not supported for video formats", isError: true);
                                            break;
                                        }
                                        try
                                        {
                                            captures.Add(
                                                await CaptureScreenshotAsync().ConfigureAwait(false)
                                            );
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.ZLogError(ex, $"Interactive capture failed.");
                                        }
                                        break;
                                    case InteractiveInputAction.ToggleRecording:
                                        try
                                        {
                                            var capture = await ToggleRecordingAsync().ConfigureAwait(false);
                                            if (capture is not null)
                                            {
                                                captures.Add(capture);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.ZLogError(ex, $"Interactive recording capture failed.");
                                        }
                                        break;
                                    case InteractiveInputAction.TogglePause:
                                        try
                                        {
                                            await TogglePauseAsync().ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.ZLogError(ex, $"Interactive recording pause failed.");
                                        }
                                        break;
                                }
                            }

                            // Function keys and other VT input can arrive across reads.
                            // Give a lone Escape a short grace period before forwarding it.
                            if (router.HasStandaloneEscape)
                            {
                                var version = Interlocked.Increment(ref escapePendingVersion);
                                ScheduleStandaloneEscape(version);
                            }

                            await connection.WriterStream.FlushAsync(lifetime.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            inputGate.Release();
                        }

                        // Rendering and conversion run after input forwarding releases
                        // its gate, so an expensive save cannot hold terminal input.
                        foreach (var capture in captures)
                        {
                            QueueSaveCapture(capture);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown.
                }
                catch (IOException)
                {
                    // The shell exited while input was being forwarded.
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, $"Interactive input or capture failed.");
                    await Console.Error.WriteLineAsync(
                        $"Interactive capture failed: {ex.Message}".AsMemory(),
                        CancellationToken.None
                    );
                }
            },
            CancellationToken.None
        );

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var initialNotificationVersion = Volatile.Read(ref notificationVersion);
                    // cmd/Clink and Starship often clear the terminal while their
                    // startup scripts run. Wait for that output to finish before
                    // drawing the host-only key guide.
                    await Task.Delay(500, lifetime.Token).ConfigureAwait(false);
                    await WaitForOutputToSettleAsync().ConfigureAwait(false);
                    if (Volatile.Read(ref notificationVersion) == initialNotificationVersion)
                    {
                        Interlocked.Exchange(ref startupIndicatorActive, 1);
                        await hostOutputGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                        try
                        {
                            await RenderPersistentIndicatorAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            hostOutputGate.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // The shell exited before its startup hint was needed.
                }
            },
            CancellationToken.None
        );

        try
        {
            while (!cancellationToken.IsCancellationRequested && !outputTask.IsCompleted)
            {
                if (connection.WaitForExit(50))
                {
                    break;
                }
            }
        }
        finally
        {
            var childExited = connection.WaitForExit(0);
            if (childExited)
            {
                // Drain the PTY before completing the recording so its final output
                // becomes part of the saved capture.
                await Task.WhenAny(outputTask, Task.Delay(500, CancellationToken.None))
                    .ConfigureAwait(false);
            }

            InteractiveCapture? capture = null;
            lock (captureGate)
            {
                if (videoFrames is not null)
                {
                    var finalScreen = Volatile.Read(ref recordingPaused) != 0
                        ? videoFrames[^1].Buffer
                        : emulator.Buffer;
                    capture = CompleteRecording(
                        videoFrames,
                        GetRecordingElapsedSeconds(),
                        finalScreen
                    );
                    videoFrames = null;
                    videoPausedDuration = 0d;
                    videoPausedAt = 0d;
                }
            }

            if (capture is not null)
            {
                Interlocked.Exchange(ref recordingIndicatorActive, 0);
                Interlocked.Exchange(ref recordingPaused, 0);
                Interlocked.Increment(ref notificationVersion);
                QueueSaveCapture(capture);
            }

            await lifetime.CancelAsync().ConfigureAwait(false);
            connection.Dispose();
            await IgnoreFailureAsync(outputTask).ConfigureAwait(false);
            await IgnoreFailureAsync(inputTask).ConfigureAwait(false);
            Task pendingCaptures;
            lock (captureQueueGate)
            {
                pendingCaptures = captureQueue;
            }
            await IgnoreFailureAsync(pendingCaptures).ConfigureAwait(false);
            // A child application can leave the outer terminal in mouse-reporting
            // mode. Reset it before restoring the host input mode so selection is
            // available after an interactive session ends.
            PtyRecorder.TryDisableTerminalMouseTracking(forwardToConsole: true, logger);
            try
            {
                await ClearHostTerminalAsync().ConfigureAwait(false);
            }
            finally
            {
                rawInput?.Dispose();
            }
            await Console.Out
                .WriteLineAsync(
                    "console2svg interactive mode finished".AsMemory(),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
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
        var descriptors = new[] { new PollFd { FileDescriptor = 0, Events = PollIn } };
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
            ["9", "1000", "1002", "1003", "1004", "1005", "1006", "1015", "1016", "9001"];
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
                    end < _pending.Length
                    && (char.IsDigit(_pending[end]) || _pending[end] == ';')
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
                    var modes = _pending
                        .ToString(index + 3, end - index - 3)
                        .Split(';');
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
