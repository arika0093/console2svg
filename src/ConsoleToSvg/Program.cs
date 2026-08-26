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

internal static partial class Program
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
            await Console.Error.WriteLineAsync(ColorizeIfSupported(OptionParser.ShortHelpText));
            return 1;
        }

        if (showHelp || options is null)
        {
            WritePagedHelp(ColorizeIfSupported(OptionParser.HelpText));
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(ThisAssembly.AssemblyInformationalVersion);
            return 0;
        }

        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.WriteLine(ColorizeIfSupported(OptionParser.ShortHelpText));
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
            $"Parsed options: Mode={options.Mode} Out={options.OutputPath} In={options.InputCastPath ?? ""} Command={options.Command ?? ""} Width={options.Width} Height={options.Height} Frame={options.Frame} Theme={options.Theme} ForeColor={options.ForeColor ?? ""} Window={options.Window} Padding={options.Padding} SaveCast={options.SaveCastPath ?? ""} Font={options.Font ?? ""} LengthAdjust={options.LengthAdjust} Prompt={options.Prompt} Header={options.Header ?? ""} NoColorEnv={options.NoColorEnv} NoDeleteEnvs={options.NoDeleteEnvs} VideoTiming={options.VideoTiming} CoalesceMs={options.OutputCoalesceMs?.ToString() ?? "auto"} SvgConverter={options.SvgConverter}"
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
            )
            {
                AutoFlush = true,
            };
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

            void WriteOutputSvg(TextWriter writer)
            {
                logger.ZLogDebug($"Rendering SVG stream. Mode={options.Mode}");
                if (options.Mode is OutputMode.Video or OutputMode.Repeat)
                {
                    AnimatedSvgRenderer.Write(writer, session, renderOptions);
                }
                else
                {
                    SvgRenderer.Write(writer, session, renderOptions);
                }
                logger.ZLogDebug($"SVG stream rendering completed.");
            }

            // Background temp-dir deletion task for the video path; awaited
            // just before the process exits so deletion completes even when
            // Windows AV makes recursive Directory.Delete slow.
            Task? tempCleanup = null;
            var savedFramesDuringVideoConversion = false;

            if (options.StdOut)
            {
                logger.ZLogDebug($"Writing SVG to stdout.");
                await using var stdoutWriter = new StreamWriter(
                    Console.OpenStandardOutput(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
                WriteOutputSvg(stdoutWriter);
                await stdoutWriter.FlushAsync(outputToken).ConfigureAwait(false);
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
                    await using var outputWriter = new StreamWriter(
                        options.OutputPath,
                        append: false,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    );
                    WriteOutputSvg(outputWriter);
                    await outputWriter.FlushAsync(outputToken).ConfigureAwait(false);
                    logger.ZLogDebug($"Output file written: {options.OutputPath}");
                }
                else
                {
                    // Route based on explicit --mode if given, otherwise infer from output extension.
                    // Explicit --mode image overrides video extensions (e.g. static GIF with --frame).
                    // No explicit mode: video extensions → frame-sequence path, others → ffmpeg image.
                    var useVideoPath = options.IsModeExplicit
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
                    logger.ZLogDebug($"Resolved converter: {converter} FfmpegPath={ffmpegPath}");

                    if (useVideoPath)
                    {
                        if (string.IsNullOrWhiteSpace(options.SaveFramesDir))
                        {
                            EnsureDirectory(options.OutputPath);
                            await SvgConverter.ConvertSvgFramesToVideoAsync(
                                    RenderFrameSvgs(
                                        session,
                                        renderOptions,
                                        options.VideoFps,
                                        outputToken,
                                        includeFallback: true
                                    ),
                                    options.VideoFps, options.OutputPath, converter, ffmpegPath,
                                    options.SizeWidth, options.SizeHeight, logger, ConsoleProgressReporter.Instance, outputToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            // Video format: save frames to a temp dir, then invoke ffmpeg.
                            var tempDir = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}");
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
                                    logger.ZLogDebug(
                                        $"Empty recording: wrote single fallback frame to {tempDir}"
                                    );
                                }

                                EnsureDirectory(options.OutputPath);
                                await SvgConverter
                                    .ConvertFramesToVideoAsync(
                                        tempDir,
                                        options.VideoFps,
                                        options.OutputPath,
                                        converter,
                                        ffmpegPath,
                                        options.SizeWidth,
                                        options.SizeHeight,
                                        logger,
                                        ConsoleProgressReporter.Instance,
                                        outputToken
                                    )
                                    .ConfigureAwait(false);

                                // The temp SVGs are exactly the frames requested by
                                // --save-frames. Copy them instead of rendering the
                                // whole recording for a second time below.
                                CopyRenderedFrameSvgs(tempDir, options.SaveFramesDir, logger);
                                savedFramesDuringVideoConversion = true;
                            }
                            finally
                            {
                                // Start temp-dir deletion on a background thread so
                                // the remaining work (writing "Generated:" message,
                                // save-frames, etc.) can proceed concurrently. The
                                // Task is awaited before the process exits so that
                                // deletion actually completes even on Windows where
                                // AV scans make recursive delete slow.
                                tempCleanup = Task.Run(() =>
                                {
                                    try
                                    {
                                        Directory.Delete(tempDir, recursive: true);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.ZLogDebug(
                                            ex,
                                            $"Failed to delete temp dir {tempDir}: {ex.Message}"
                                        );
                                    }
                                });
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
                            await SvgConverter
                                .ConvertSvgToImageAsync(
                                    tempSvg,
                                    options.OutputPath,
                                    converter,
                                    ffmpegPath,
                                    options.SizeWidth,
                                    options.SizeHeight,
                                    logger,
                                    ConsoleProgressReporter.Instance,
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
                                    logger.ZLogDebug(
                                        ex,
                                        $"Failed to delete temp SVG {tempSvg}: {ex.Message}"
                                    );
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(options.SaveFramesDir) && !savedFramesDuringVideoConversion)
            {
                await SaveFramesAsync(
                        session,
                        renderOptions,
                        options.SaveFramesDir,
                        options.VideoFps,
                        logger,
                        outputToken
                    )
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
                if (tempCleanup is not null)
                {
                    await tempCleanup.ConfigureAwait(false);
                }
                return 0;
            }

            await Console.Error.WriteLineAsync(
                options.StdOut ? "Generated: (stdout)" : $"Generated: {options.OutputPath}"
            );
            if (tempCleanup is not null)
            {
                await tempCleanup.ConfigureAwait(false);
            }
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
}
