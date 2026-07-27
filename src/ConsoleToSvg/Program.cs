using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg;

internal static class Program
{
    private const int DefaultWidth = 100;
    private const int DefaultHeight = 24;

    public static async Task<int> Main(string[] args)
    {
        var parseResult = OptionParser.TryParse(
            args,
            out var options,
            out var error,
            out var showHelp
        );
        if (!parseResult)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(OptionParser.ShortHelpText);
            return 1;
        }

        if (showHelp || options is null)
        {
            Console.WriteLine(OptionParser.HelpText);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(ThisAssembly.AssemblyInformationalVersion);
            return 0;
        }

        if (options.InstallDependencies)
        {
            await DependencyInstaller.InstallFfmpegAsync().ConfigureAwait(false);
            return 0;
        }

        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.WriteLine(OptionParser.ShortHelpText);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Prompt))
        {
            options.Prompt = GetDefaultPrompt();
        }

        using var loggerFactory = CreateLoggerFactory(options.Verbose, options.VerboseLogPath);
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.Program");
        logger.ZLogDebug(
            $"Application started. Version={ThisAssembly.AssemblyInformationalVersion} OS={Environment.OSVersion.Platform} Arch={RuntimeInformation.ProcessArchitecture}"
        );
        logger.ZLogDebug(
            $"Verbose={options.Verbose} VerboseLogPath={options.VerboseLogPath ?? "(default)"} Args={string.Join(' ', args)}"
        );
        logger.ZLogDebug(
            $"Parsed options: Mode={options.Mode} Out={options.OutputPath} In={options.InputCastPath ?? ""} Command={options.Command ?? ""} Width={options.Width} Height={options.Height} Frame={options.Frame} Theme={options.Theme} ForeColor={options.ForeColor ?? ""} Window={options.Window} Padding={options.Padding} SaveCast={options.SaveCastPath ?? ""} Font={options.Font ?? ""} LengthAdjust={options.LengthAdjust} Prompt={options.Prompt} Header={options.Header ?? ""} NoColorEnv={options.NoColorEnv} NoDeleteEnvs={options.NoDeleteEnvs} VideoTiming={options.VideoTiming} CoalesceMs={options.OutputCoalesceMs} SvgConverter={options.SvgConverter}"
        );
        using var environmentScope = ApplyProcessEnvironmentOverrides(options, logger);

        var canceledByCtrlC = false;
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            canceledByCtrlC = true;
            cancellationTokenSource.Cancel();
            logger.ZLogDebug($"Cancellation requested by Ctrl+C.");
        };

        if (options.Timeout.HasValue)
        {
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(options.Timeout.Value));
            logger.ZLogDebug($"Timeout set: {options.Timeout.Value} seconds.");
        }

        if (options.StdOut)
        {
            // Redirect Console.Out → stderr before recording so that any third-party library
            // debug messages written via Console.Write/WriteLine (e.g. Quick.PtyNet's
            // "Waiting on {pid}" / "Wait succeeded" from its ChildWatcherThreadProc)
            // are sent to stderr instead of polluting the SVG output pipe.
            var stderrWriter = new StreamWriter(
                Console.OpenStandardError(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true
            ) { AutoFlush = true };
            Console.SetOut(stderrWriter);
            logger.ZLogDebug($"Console.Out redirected to stderr for --stdout mode.");
        }

        try
        {
            // Pre-check: verify ffmpeg and converter availability BEFORE starting
            // the recording, so a missing tool surfaces immediately instead of
            // wasting the recorded session. This also applies to interactive
            // capture, where frames cannot be recovered after a failed save.
            // Only raster/video outputs need conversion tools; pure SVG output
            // (including --stdout) does not.
            if (!options.StdOut)
            {
                var preCheckExt = Path.GetExtension(options.OutputPath)
                    .TrimStart('.')
                    .ToLowerInvariant();
                if (
                    !string.IsNullOrEmpty(preCheckExt)
                    && !string.Equals(preCheckExt, "svg", StringComparison.Ordinal)
                )
                {
                    // Resolve ffmpeg and the converter early; the lazy caches survive
                    // to the post-recording conversion step, so this is not duplicated work.
                    var preFfmpegPath = FindFfmpegExecutable();
                    SvgConverter.SetFfmpegPath(preFfmpegPath);

                    SvgConverter.VerifyConversionPipeline(
                        options.SvgConverter,
                        RequiresFfmpeg(options, preCheckExt),
                        logger
                    );
                    logger.ZLogDebug(
                        $"Pre-conversion check passed: converter verified for .{preCheckExt} output."
                    );
                }
            }

            if (options.Interactive)
            {
                return await RunInteractiveAsync(
                        options,
                        loggerFactory,
                        cancellationTokenSource.Token
                    )
                    .ConfigureAwait(false);
            }

            var session = await LoadOrRecordAsync(
                    options,
                    loggerFactory,
                    cancellationTokenSource.Token
                )
                .ConfigureAwait(false);
            var wasCanceled = cancellationTokenSource.IsCancellationRequested;
            var outputToken = wasCanceled ? CancellationToken.None : cancellationTokenSource.Token;
            logger.ZLogDebug(
                $"Recording loaded. Events={session.Events.Count} Width={session.Header.width} Height={session.Header.height}"
            );

            if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            {
                logger.ZLogDebug($"Saving asciicast to {options.SaveCastPath}");
                await AsciicastWriter
                    .WriteToFileAsync(options.SaveCastPath, session, outputToken)
                    .ConfigureAwait(false);
                logger.ZLogDebug($"Saved asciicast to {options.SaveCastPath}");
            }

            var renderOptions = SvgRenderOptionsFactory.Create(options);
            logger.ZLogDebug($"Rendering SVG. Mode={options.Mode}");
            var svg =
                options.Mode is OutputMode.Video or OutputMode.Repeat
                    ? AnimatedSvgRenderer.Render(session, renderOptions)
                    : SvgRenderer.Render(session, renderOptions);
            logger.ZLogDebug($"Rendering completed. SvgLength={svg.Length}");

            if (options.StdOut)
            {
                logger.ZLogDebug($"Writing SVG to stdout.");
                await using var stdoutWriter = new StreamWriter(
                    Console.OpenStandardOutput(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
                await stdoutWriter.WriteAsync(svg).ConfigureAwait(false);
                logger.ZLogDebug($"SVG written to stdout.");
            }
            else
            {
                var outputExt = Path.GetExtension(options.OutputPath)
                    .TrimStart('.')
                    .ToLowerInvariant();

                if (string.IsNullOrEmpty(outputExt) || outputExt == "svg")
                {
                    // SVG output – existing behaviour
                    EnsureDirectory(options.OutputPath);
                    logger.ZLogDebug($"Writing output file: {options.OutputPath}");
                    await File.WriteAllTextAsync(
                            options.OutputPath,
                            svg,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                            outputToken
                        )
                        .ConfigureAwait(false);
                    logger.ZLogDebug($"Output file written: {options.OutputPath}");
                }
                else
                {
                    // Route based on explicit --mode if given, otherwise infer from output extension.
                    // Explicit --mode image overrides video extensions (e.g. static GIF with --frame).
                    // No explicit mode: video extensions → frame-sequence path, others → ffmpeg image.
                    var useVideoPath =
                        options.IsModeExplicit
                            ? options.Mode is OutputMode.Video or OutputMode.Repeat
                            : IsVideoFormat(outputExt);

                    // Resolve the SVG → raster converter once. This detects whether
                    // ffmpeg has librsvg support and falls back to rsvg-convert /
                    // bundled resvg as needed. FfmpegPath is still needed for the PNG →
                    // final-format step in the fallback pipeline, so we resolve it
                    // unconditionally (it is simply unused when a fallback converter
                    // produces PNG directly).
                    var ffmpegPath = FindFfmpegExecutable();
                    SvgConverter.SetFfmpegPath(ffmpegPath);
                    var converter = SvgConverter.ResolveConverter(
                        options.SvgConverter,
                        ffmpegAvailableOverride: SvgConverter.IsFfmpegAvailable,
                        logger
                    );
                    logger.ZLogDebug(
                        $"Resolved converter: {converter} FfmpegPath={ffmpegPath}"
                    );

                    if (useVideoPath)
                    {
                        // Video format: save frames to a temp dir, then invoke ffmpeg.
                        var tempDir = Path.Combine(
                            Path.GetTempPath(),
                            $"c2s-{Guid.NewGuid():N}"
                        );
                        try
                        {
                            logger.ZLogDebug($"Video output: saving frames to temp dir {tempDir}");
                            var frameCount = await SaveFramesAsync(
                                    session,
                                    renderOptions,
                                    tempDir,
                                    options.VideoFps,
                                    logger,
                                    outputToken
                                )
                                .ConfigureAwait(false);

                            // Guard against empty recordings: ensure at least one frame exists
                            // so ffmpeg receives valid input (e.g. commands that exit without output).
                            if (frameCount == 0)
                            {
                                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                                var fallbackSvg = SvgRenderer.Render(session, renderOptions);
                                await File.WriteAllTextAsync(
                                        Path.Combine(tempDir, "frame-0000.svg"),
                                        fallbackSvg,
                                        utf8,
                                        outputToken
                                    )
                                    .ConfigureAwait(false);
                                logger.ZLogDebug($"Empty recording: wrote single fallback frame to {tempDir}");
                            }

                            EnsureDirectory(options.OutputPath);
                            await SvgConverter.ConvertFramesToVideoAsync(
                                    tempDir,
                                    options.VideoFps,
                                    options.OutputPath,
                                    converter,
                                    ffmpegPath,
                                    options.SizeWidth,
                                    options.SizeHeight,
                                    logger,
                                    outputToken
                                )
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            if (Directory.Exists(tempDir))
                            {
                                try
                                {
                                    Directory.Delete(tempDir, recursive: true);
                                }
                                catch (Exception ex)
                                {
                                    logger.ZLogDebug(ex, $"Failed to delete temp dir {tempDir}: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Raster image (png, jpg, …): render a static SVG then convert via ffmpeg.
                        // Always use the static renderer regardless of --mode, so the output reflects
                        // the last terminal frame by default (or the --frame index if specified).
                        var staticSvg = SvgRenderer.Render(session, renderOptions);
                        var tempSvg = Path.Combine(
                            Path.GetTempPath(),
                            $"c2s-{Guid.NewGuid():N}.svg"
                        );
                        try
                        {
                            logger.ZLogDebug($"Image output: writing temp SVG to {tempSvg}");
                            await File.WriteAllTextAsync(
                                    tempSvg,
                                    staticSvg,
                                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                    outputToken
                                )
                                .ConfigureAwait(false);
                            EnsureDirectory(options.OutputPath);
                            await SvgConverter.ConvertSvgToImageAsync(
                                    tempSvg,
                                    options.OutputPath,
                                    converter,
                                    ffmpegPath,
                                    options.SizeWidth,
                                    options.SizeHeight,
                                    logger,
                                    outputToken
                                )
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            if (File.Exists(tempSvg))
                            {
                                try
                                {
                                    File.Delete(tempSvg);
                                }
                                catch (Exception ex)
                                {
                                    logger.ZLogDebug(ex, $"Failed to delete temp SVG {tempSvg}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(options.SaveFramesDir))
            {
                await SaveFramesAsync(session, renderOptions, options.SaveFramesDir, options.VideoFps, logger, outputToken)
                    .ConfigureAwait(false);
            }

            if (wasCanceled)
            {
                var cause = GetCancellationCause(options, canceledByCtrlC);
                logger.ZLogDebug($"Recording stopped. Cause={cause}");
                var message = options.StdOut
                    ? "Generated (partial): (stdout)"
                    : $"Generated (partial): {options.OutputPath}";
                await Console.Error.WriteLineAsync(message.AsMemory(), CancellationToken.None);
                return 0;
            }

            await Console.Error.WriteLineAsync(
                options.StdOut ? "Generated: (stdout)" : $"Generated: {options.OutputPath}"
            );
            return 0;
        }
        catch (OperationCanceledException)
        {
            var cause = GetCancellationCause(options, canceledByCtrlC);
            logger.ZLogDebug($"Execution canceled. Cause={cause}");
            await Console.Error.WriteLineAsync("Canceled.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"Unhandled exception occurred: {ex.Message}");
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static async Task<RecordingSession> LoadOrRecordAsync(
        AppOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.LoadOrRecord");
        if (!string.IsNullOrWhiteSpace(options.InputCastPath))
        {
            logger.ZLogDebug($"Input source: asciicast file. Path={options.InputCastPath}");
            return await AsciicastReader
                .ReadFromFileAsync(options.InputCastPath, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(options.Command))
        {
            var ptyWidth = ResolveSize(options.Width, options.WidthAdjust, TryGetConsoleWidth, DefaultWidth);
            var ptyHeight = ResolveSize(options.Height, options.HeightAdjust, TryGetConsoleHeight, DefaultHeight);

            if (options.Mode == OutputMode.Repeat)
            {
                logger.ZLogDebug(
                    $"Input source: repeat command. Command={options.Command} Width={ptyWidth} Height={ptyHeight} Fps={options.VideoFps}"
                );
                return await RepeatRecorder
                    .RecordAsync(
                        options.Command,
                        ptyWidth,
                        ptyHeight,
                        options.VideoFps,
                        cancellationToken,
                        loggerFactory.CreateLogger("ConsoleToSvg.RepeatRecorder"),
                        noDeleteEnvs: options.NoDeleteEnvs
                    )
                    .ConfigureAwait(false);
            }
            logger.ZLogDebug(
                $"Input source: PTY command. Command={options.Command} Width={ptyWidth} Height={ptyHeight}"
            );
            return await PtyRecorder
                .RecordAsync(
                    options.Command,
                    ptyWidth,
                    ptyHeight,
                    cancellationToken,
                    loggerFactory.CreateLogger("ConsoleToSvg.PtyRecorder"),
                    forwardToConsole: !options.StdOut,
                    noDeleteEnvs: options.NoDeleteEnvs,
                    replaySavePath: options.ReplaySavePath,
                    replayPath: options.ReplayPath,
                    outputCoalesceMs: options.OutputCoalesceMs
                )
                .ConfigureAwait(false);
        }

        if (!Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                """
                No input source specified.
                Usage: 
                  console2svg "your-command with args" [options]
                  console2svg [options] -- your-command with args 
                  your-command with args | console2svg [options]
                For more details, see --help.
                """
            );
        }

        var pipeWidth = options.Width ?? TryGetConsoleWidth() ?? DefaultWidth;
        var pipeHeight = options.Height ?? TryGetConsoleHeight() ?? DefaultHeight;
        logger.ZLogDebug($"Input source: stdin pipe. Width={pipeWidth} Height={pipeHeight}");
        return await PipeRecorder
            .RecordAsync(
                Console.OpenStandardInput(),
                pipeWidth,
                pipeHeight,
                cancellationToken,
                loggerFactory.CreateLogger("ConsoleToSvg.PipeRecorder")
            )
            .ConfigureAwait(false);
    }

    private static async Task<int> RunInteractiveAsync(
        AppOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var renderOptions = SvgRenderOptionsFactory.Create(options);
        var width = ResolveSize(options.Width, options.WidthAdjust, TryGetConsoleWidth, DefaultWidth);
        var height = ResolveSize(options.Height, options.HeightAdjust, TryGetConsoleHeight, DefaultHeight);
        var theme = Theme.Resolve(renderOptions.Theme);
        if (!string.IsNullOrWhiteSpace(renderOptions.BackColor))
        {
            theme = theme.WithBackground(renderOptions.BackColor);
        }
        if (!string.IsNullOrWhiteSpace(renderOptions.ForeColor))
        {
            theme = theme.WithForeground(renderOptions.ForeColor);
        }

        await InteractiveRecorder
            .RunAsync(
                width,
                height,
                theme,
                Encoding.ASCII.GetBytes("\u001b[21~"), // F10
                Encoding.ASCII.GetBytes("\u001b[20~"), // F9
                Encoding.ASCII.GetBytes("\u001b[24~"), // F12
                options.NoDeleteEnvs,
                options.DelimitedCommand,
                options.DelimitedCommand is null or { Length: 0 },
                IsInteractiveRecordingFormat(options.OutputPath),
                async capture =>
                {
                    var outputPath = GetInteractiveOutputPath(options.OutputPath);
                    await WriteInteractiveCaptureAsync(
                            capture,
                            outputPath,
                            renderOptions,
                            options,
                            loggerFactory.CreateLogger("ConsoleToSvg.InteractiveCapture")
                        )
                        .ConfigureAwait(false);
                    return "Saved";
                },
                cancellationToken,
                loggerFactory.CreateLogger("ConsoleToSvg.InteractiveRecorder")
            )
            .ConfigureAwait(false);
        return 0;
    }

    private static string GetInteractiveOutputPath(string configuredPath)
    {
        var directory = Path.GetDirectoryName(configuredPath);
        var extension = Path.GetExtension(configuredPath);
        var name = Path.GetFileNameWithoutExtension(configuredPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "output";
        }
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".svg";
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var candidateName = $"{name}_{stamp}{extension}";
        var candidate = string.IsNullOrEmpty(directory)
            ? candidateName
            : Path.Combine(directory, candidateName);
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidateName = $"{name}_{stamp}_{suffix}{extension}";
            candidate = string.IsNullOrEmpty(directory)
                ? candidateName
                : Path.Combine(directory, candidateName);
            suffix++;
        }

        return candidate;
    }

    private static async Task WriteInteractiveCaptureAsync(
        InteractiveCapture capture,
        string outputPath,
        SvgRenderOptions renderOptions,
        AppOptions options,
        ILogger logger
    )
    {
        EnsureDirectory(outputPath);
        var extension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || extension == "svg")
        {
            var svg = capture.IsVideo
                ? AnimatedSvgRenderer.RenderFrames(capture.Frames, renderOptions)
                : SvgRenderer.Render(capture.Screen, renderOptions);
            await File.WriteAllTextAsync(
                    outputPath,
                    svg,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    CancellationToken.None
                )
                .ConfigureAwait(false);

            if (capture.IsVideo && !string.IsNullOrWhiteSpace(options.SaveFramesDir))
            {
                var framesPath = Path.Combine(
                    options.SaveFramesDir,
                    Path.GetFileNameWithoutExtension(outputPath)
                );
                Directory.CreateDirectory(framesPath);
                var frames = SampleInteractiveFrames(
                    FilterInteractiveFrames(capture.Frames, renderOptions),
                    options.VideoFps
                );
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                for (var i = 0; i < frames.Count; i++)
                {
                    await File.WriteAllTextAsync(
                            Path.Combine(framesPath, $"frame-{i:D4}.svg"),
                            SvgRenderer.Render(frames[i].Buffer, renderOptions),
                            utf8,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
            }
            return;
        }

        var ffmpegPath = FindFfmpegExecutable();
        SvgConverter.SetFfmpegPath(ffmpegPath);
        var converter = SvgConverter.ResolveConverter(
            options.SvgConverter,
            ffmpegAvailableOverride: SvgConverter.IsFfmpegAvailable,
            logger
        );
        var preserveFrames = capture.IsVideo && !string.IsNullOrWhiteSpace(options.SaveFramesDir);
        var preservedFramesPath = preserveFrames
            ? Path.Combine(options.SaveFramesDir!, Path.GetFileNameWithoutExtension(outputPath))
            : null;
        // Conversion may replace SVGs with PNG intermediates. Keep that work
        // separate from --save-frames so the requested SVG frames survive.
        var workPath = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}");
        try
        {
            if (capture.IsVideo)
            {
                Directory.CreateDirectory(workPath);
                var frames = SampleInteractiveFrames(
                    FilterInteractiveFrames(capture.Frames, renderOptions),
                    options.VideoFps
                );
                for (var i = 0; i < frames.Count; i++)
                {
                    var frameSvg = SvgRenderer.Render(frames[i].Buffer, renderOptions);
                    var fileName = $"frame-{i:D4}.svg";
                    var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    await File.WriteAllTextAsync(
                            Path.Combine(workPath, fileName), frameSvg, utf8, CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    if (preservedFramesPath is not null)
                    {
                        Directory.CreateDirectory(preservedFramesPath);
                        await File.WriteAllTextAsync(
                                Path.Combine(preservedFramesPath, fileName), frameSvg, utf8, CancellationToken.None
                            )
                            .ConfigureAwait(false);
                    }
                }

                await SvgConverter.ConvertFramesToVideoAsync(
                        workPath,
                        options.VideoFps,
                        outputPath,
                        converter,
                        ffmpegPath,
                        options.SizeWidth,
                        options.SizeHeight,
                        logger,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                var svg = SvgRenderer.Render(capture.Screen, renderOptions);
                var temporarySvg = workPath + ".svg";
                await File.WriteAllTextAsync(
                        temporarySvg,
                        svg,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                await SvgConverter.ConvertSvgToImageAsync(
                        temporarySvg,
                        outputPath,
                        converter,
                        ffmpegPath,
                        options.SizeWidth,
                        options.SizeHeight,
                        logger,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }

        finally
        {
            try
            {
                if (Directory.Exists(workPath))
                {
                    Directory.Delete(workPath, recursive: true);
                }
                else if (File.Exists(workPath + ".svg"))
                {
                    File.Delete(workPath + ".svg");
                }
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to remove interactive conversion work path {workPath}.");
            }
        }
    }

    private static IReadOnlyList<TerminalFrame> SampleInteractiveFrames(
        IReadOnlyList<TerminalFrame> frames,
        double fps
    )
    {
        if (frames.Count <= 1 || fps <= 0d)
        {
            return frames;
        }

        var interval = 1d / fps;
        var duration = frames[frames.Count - 1].Time;
        var count = (int)Math.Floor(duration * fps) + 1;
        var sampled = new List<TerminalFrame>(count + 1);
        var sourceIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var time = index * interval;
            while (
                sourceIndex + 1 < frames.Count
                && frames[sourceIndex + 1].Time <= time + 1e-9
            )
            {
                sourceIndex++;
            }

            sampled.Add(new TerminalFrame(time, frames[sourceIndex].Buffer));
        }

        if (!ReferenceEquals(sampled[sampled.Count - 1].Buffer, frames[frames.Count - 1].Buffer))
        {
            sampled.Add(new TerminalFrame(duration, frames[frames.Count - 1].Buffer));
        }

        return sampled;
    }

    private static IReadOnlyList<TerminalFrame> FilterInteractiveFrames(
        IReadOnlyList<TerminalFrame> frames,
        SvgRenderOptions renderOptions
    )
    {
        if (
            frames.Count == 0
            || (!renderOptions.TimeStart.HasValue && !renderOptions.TimeEnd.HasValue)
        )
        {
            return frames;
        }

        var hasStart = renderOptions.TimeStart.HasValue;
        var start = renderOptions.TimeStart ?? double.MinValue;
        var end = renderOptions.TimeEnd ?? double.MaxValue;
        var filtered = new List<TerminalFrame>(frames.Count);
        TerminalFrame? lastBeforeStart = null;
        foreach (var frame in frames)
        {
            if (frame.Time < start - 1e-9)
            {
                lastBeforeStart = frame;
                continue;
            }

            if (frame.Time > end + 1e-9)
            {
                break;
            }

            filtered.Add(frame);
        }

        if (lastBeforeStart is not null)
        {
            // Carry the visible terminal state into the requested range rather
            // than extending the clip backwards to its previous update.
            filtered.Insert(0, new TerminalFrame(start, lastBeforeStart.Buffer));
        }

        if (filtered.Count == 0)
        {
            return [frames[0]];
        }

        var baseTime = hasStart ? start : filtered[0].Time;
        for (var i = 0; i < filtered.Count; i++)
        {
            filtered[i] = new TerminalFrame(filtered[i].Time - baseTime, filtered[i].Buffer);
        }

        return filtered;
    }

    private static string GetCancellationCause(AppOptions options, bool canceledByCtrlC)
    {
        if (canceledByCtrlC)
        {
            return "Ctrl+C";
        }

        if (options.Timeout.HasValue)
        {
            return $"timeout ({options.Timeout.Value}s)";
        }

        return "cancellation";
    }

    private static ILoggerFactory CreateLoggerFactory(bool verbose, string? logPath)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            if (verbose)
            {
                var path = string.IsNullOrWhiteSpace(logPath) ? "console2svg.log" : logPath;
                builder.AddZLoggerFile(
                    path,
                    options =>
                    {
                        options.FileShared = false;
                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter(
                                $"[{0:local}] ",
                                (in template, in info) => template.Format(info.Timestamp)
                            );
                        });
                    }
                );
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.None);
            }
        });
    }

    private static int? TryGetConsoleWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            return w > 0 ? w : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetConsoleHeight()
    {
        try
        {
            var h = Console.WindowHeight;
            return h > 0 ? h : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveSize(
        int? explicitValue,
        bool adjust,
        Func<int?> detectTerminalSize,
        int defaultValue
    )
    {
        if (explicitValue.HasValue)
        {
            return explicitValue.Value;
        }

        return adjust ? detectTerminalSize() ?? defaultValue : defaultValue;
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    // Video file extensions that are handled by the frame-sequence → ffmpeg path.
    // GIF is included here because the primary use-case for terminal recordings is
    // an animated GIF; users who want a static GIF can specify --mode image separately.
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "webm", "avi", "mov", "mkv", "ogv", "flv", "ts", "wmv", "m4v", "gif",
    };

    private static bool IsVideoFormat(string extension) => VideoExtensions.Contains(extension);

    private static bool IsInteractiveRecordingFormat(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).TrimStart('.');
        return string.IsNullOrEmpty(extension)
            || string.Equals(extension, "svg", StringComparison.OrdinalIgnoreCase)
            || IsVideoFormat(extension);
    }

    /// <summary>
    /// Determines whether ffmpeg is required to produce the final output format.
    /// Video formats always need ffmpeg; PNG can be produced via rsvg-convert or
    /// bundled resvg alone; all other raster formats (gif, jpg, webp, etc.) also need ffmpeg.
    /// If <see cref="AppOptions.IsModeExplicit"/> is set, the explicit mode takes
    /// precedence over the extension (e.g. <c>--mode image</c> for a static .gif).
    /// </summary>
    private static bool RequiresFfmpeg(AppOptions options, string extension)
    {
        var useVideoPath = options.IsModeExplicit
            ? options.Mode is OutputMode.Video or OutputMode.Repeat
            : IsVideoFormat(extension);
        var isPngOutput = string.Equals(extension, "png", StringComparison.Ordinal);
        return useVideoPath || !isPngOutput;
    }

    /// <summary>
    /// Finds the ffmpeg executable to use for format conversion.
    /// Preference order: application ffmpeg/ subdirectory, binary next to the host (legacy bundled), then PATH.
    /// </summary>
    private static string FindFfmpegExecutable()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        // AppContext.BaseDirectory is the dotnet tool directory. Environment.ProcessPath
        // points at the dotnet host for framework-dependent tools, so it cannot be used
        // as the install location for --install-deps.
        var appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appDir))
        {
            // 1. ffmpeg installed/bundled in a subdirectory. This keeps ffmpeg off the
            //    user's PATH while still making it available to console2svg.
            var bundledSubDir = Path.Combine(appDir, "ffmpeg", exeName);
            if (File.Exists(bundledSubDir))
            {
                return bundledSubDir;
            }
        }

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrEmpty(exeDir))
        {
            // 2. Legacy bundled layout: ffmpeg next to the process executable.
            var bundled = Path.Combine(exeDir, exeName);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        // 3. Rely on PATH
        return exeName;
    }

    /// <returns>The number of frame files written to <paramref name="directory"/>.</returns>
    private static async Task<int> SaveFramesAsync(
        RecordingSession session,
        SvgRenderOptions baseOptions,
        string directory,
        double fps,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var eventCount = session.Events.Count;
        logger.ZLogDebug($"Saving individual frames to {directory}. Events={eventCount} Fps={fps}");

        if (fps > 0 && eventCount > 0)
        {
            // Sample frames at exactly fps intervals so frame count = floor(totalTime * fps) + 1
            var totalTime = session.Events[eventCount - 1].Time;
            var totalFrames = (int)Math.Floor(totalTime * fps) + 1;
            var interval = 1.0 / fps;

            // When a time range is specified, skip frames outside [TimeStart, TimeEnd].
            var rangeStart = baseOptions.TimeStart ?? 0.0;
            var rangeEnd = baseOptions.TimeEnd ?? totalTime;

            var savedCount = 0;
            for (var f = 0; f < totalFrames; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var t = f * interval;
                if (t < rangeStart - 1e-9)
                {
                    continue;
                }
                if (t > rangeEnd + 1e-9)
                {
                    break;
                }

                // Find the last event index at or before time t
                var eventIndex = -1;
                for (var i = 0; i < eventCount; i++)
                {
                    if (session.Events[i].Time <= t + 1e-9)
                        eventIndex = i;
                    else
                        break;
                }

                baseOptions.Frame = eventIndex >= 0 ? eventIndex : 0;
                var frameSvg = SvgRenderer.Render(session, baseOptions);
                var framePath = Path.Combine(directory, $"frame-{savedCount:D4}.svg");
                await File.WriteAllTextAsync(framePath, frameSvg, utf8, cancellationToken)
                    .ConfigureAwait(false);
                savedCount++;
            }

            baseOptions.Frame = null;
            logger.ZLogDebug($"Saved {savedCount} frames to {directory}");
            await Console.Error.WriteLineAsync(
                $"Saved {savedCount} frames to {directory}".AsMemory(),
                CancellationToken.None
            );
            return savedCount;
        }
        else
        {
            // No fps specified: save one file per unique visual state
            var savedCount = 0;
            string? previousSvg = null;

            for (var i = 0; i < eventCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip frames outside the time range if specified.
                var eventTime = session.Events[i].Time;
                if (baseOptions.TimeStart.HasValue && eventTime < baseOptions.TimeStart.Value - 1e-9)
                {
                    continue;
                }
                if (baseOptions.TimeEnd.HasValue && eventTime > baseOptions.TimeEnd.Value + 1e-9)
                {
                    break;
                }

                baseOptions.Frame = i;
                var frameSvg = SvgRenderer.Render(session, baseOptions);

                if (frameSvg == previousSvg)
                {
                    continue;
                }

                previousSvg = frameSvg;
                var framePath = Path.Combine(directory, $"frame-{savedCount:D4}.svg");
                await File.WriteAllTextAsync(framePath, frameSvg, utf8, cancellationToken)
                    .ConfigureAwait(false);
                savedCount++;
            }

            baseOptions.Frame = null;
            logger.ZLogDebug($"Saved {savedCount} unique frames (of {eventCount} events) to {directory}");
            await Console.Error.WriteLineAsync(
                $"Saved {savedCount} frames to {directory}".AsMemory(),
                CancellationToken.None
            );
            return savedCount;
        }
    }

    private static string GetDefaultPrompt()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "$";
        }

        try
        {
            return GetEffectiveUserId() == 0 ? "#" : "$";
        }
        catch
        {
            return "$";
        }
    }

    private static uint GetEffectiveUserId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return 1;
        }

        return geteuid();
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static IDisposable ApplyProcessEnvironmentOverrides(AppOptions options, ILogger logger)
    {
        var scope = new EnvironmentVariableScope(logger);

        // Ensure DOTNET_EnableWriteXorExecute=0 is set to prevent potential issues with
        // memory protection on some platforms, especially when dynamic code is involved.
        scope.Set("DOTNET_EnableWriteXorExecute", "0");

        if (!string.IsNullOrWhiteSpace(options.Command) && !options.NoColorEnv)
        {
            logger.ZLogDebug($"Applying color-related environment overrides.");
            // Ensure color-capable settings even on CI runners where TERM is unset/dumb.
            scope.Set("TERM", "xterm-256color");
            scope.Set("COLORTERM", "truecolor");
            scope.Set("FORCE_COLOR", "3");
        }

        return scope;
    }

    private sealed class EnvironmentVariableScope(ILogger logger) : IDisposable
    {
        private readonly Dictionary<string, (bool Exists, string? Value)> _originalValues = new(
            StringComparer.Ordinal
        );
        private readonly List<string> _appliedKeys = [];
        private bool _disposed;

        public void Set(string key, string value) => Apply(key, value);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (var i = _appliedKeys.Count - 1; i >= 0; i--)
            {
                var key = _appliedKeys[i];
                var original = _originalValues[key];
                try
                {
                    Environment.SetEnvironmentVariable(
                        key,
                        original.Exists ? original.Value : null
                    );
                }
                catch (Exception ex)
                {
                    logger.ZLogDebug(ex, $"Failed to restore environment variable: {key}");
                }
            }

            _disposed = true;
            logger.ZLogDebug($"Restored temporary environment variable overrides.");
        }

        private void Apply(string key, string? value)
        {
            if (!_originalValues.ContainsKey(key))
            {
                var original = Environment.GetEnvironmentVariable(key);
                _originalValues[key] = (original is not null, original);
                _appliedKeys.Add(key);
            }

            try
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to update environment variable: {key}");
            }
        }
    }
}
