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
            var ptyWidth = ResolveSize(
                options.Width,
                options.WidthAdjust,
                TryGetConsoleWidth,
                DefaultWidth
            );
            var ptyHeight = ResolveSize(
                options.Height,
                options.HeightAdjust,
                TryGetConsoleHeight,
                DefaultHeight
            );

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

        var pipeWidth = ResolveSize(
            options.Width,
            options.WidthAdjust,
            TryGetConsoleWidth,
            DefaultWidth
        );
        var pipeHeight = ResolveSize(
            options.Height,
            options.HeightAdjust,
            TryGetConsoleHeight,
            DefaultHeight
        );
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
        var width = ResolveSize(
            options.Width,
            options.WidthAdjust,
            TryGetConsoleWidth,
            DefaultWidth
        );
        var height = ResolveSize(
            options.Height,
            options.HeightAdjust,
            TryGetConsoleHeight,
            DefaultHeight
        );
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
                !IsVideoFormat(Path.GetExtension(options.OutputPath).TrimStart('.')),
                async capture =>
                {
                    var outputPath = GetInteractiveOutputPath(
                        options.OutputPath,
                        capture.IsVideo ? null : Path.GetExtension(options.OutputPath)
                    );
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

    private static string GetInteractiveOutputPath(
        string configuredPath,
        string? extensionOverride = null
    )
    {
        var directory = Path.GetDirectoryName(configuredPath);
        var extension = extensionOverride ?? Path.GetExtension(configuredPath);
        var name = Path.GetFileNameWithoutExtension(configuredPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "output";
        }
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".svg";
        }

        var stamp = DateTime.Now.ToString(
            "yyyyMMdd_HHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture
        );
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
                            Path.Combine(workPath, fileName),
                            frameSvg,
                            utf8,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                    if (preservedFramesPath is not null)
                    {
                        Directory.CreateDirectory(preservedFramesPath);
                        await File.WriteAllTextAsync(
                                Path.Combine(preservedFramesPath, fileName),
                                frameSvg,
                                utf8,
                                CancellationToken.None
                            )
                            .ConfigureAwait(false);
                    }
                }

                await SvgConverter
                    .ConvertFramesToVideoAsync(
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
                await SvgConverter
                    .ConvertSvgToImageAsync(
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
                logger.ZLogDebug(
                    ex,
                    $"Failed to remove interactive conversion work path {workPath}."
                );
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
            while (sourceIndex + 1 < frames.Count && frames[sourceIndex + 1].Time <= time + 1e-9)
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
}
