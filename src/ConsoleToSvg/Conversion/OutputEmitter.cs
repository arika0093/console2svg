using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg.Conversion;

internal static class OutputEmitter
{
    public static async Task EmitAsync(
        RecordingSession session,
        AppOptions options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var plan = OutputPlan.Create(options);
        var renderOptions = SvgRenderOptions.FromAppOptions(options);

        if (plan.IsSvg)
        {
            var svg =
                options.Mode is OutputMode.Video or OutputMode.Repeat
                    ? AnimatedSvgRenderer.Render(session, renderOptions)
                    : SvgRenderer.Render(session, renderOptions);

            logger.LogDebug("Rendering completed. SvgLength={SvgLength}", svg.Length);

            if (options.StdOut)
            {
                logger.LogDebug("Writing SVG to stdout.");
                await using var stdoutWriter = new StreamWriter(
                    Console.OpenStandardOutput(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
                await stdoutWriter.WriteAsync(svg).ConfigureAwait(false);
                logger.LogDebug("SVG written to stdout.");
            }
            else
            {
                EnsureDirectory(options.OutputPath);
                logger.LogDebug("Writing SVG output file: {OutputPath}", options.OutputPath);
                await File.WriteAllTextAsync(
                        options.OutputPath,
                        svg,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(options.SaveFramesDir))
            {
                await SvgFrameExporter.SaveFramesAsync(
                        session,
                        SvgRenderOptions.FromAppOptions(options),
                        options.SaveFramesDir!,
                        options.VideoFps,
                        logger,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return;
        }

        EnsureDirectory(options.OutputPath);

        if (plan.RequiresAnimatedFrames)
        {
            await EmitAnimatedRasterAsync(session, options, logger, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await EmitStaticRasterAsync(session, renderOptions, plan, options.OutputPath, logger, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.SaveFramesDir))
        {
            await SvgFrameExporter.SaveFramesAsync(
                    session,
                    SvgRenderOptions.FromAppOptions(options),
                    options.SaveFramesDir!,
                    options.VideoFps,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    internal static async Task EmitStaticRasterAsync(
        RecordingSession session,
        SvgRenderOptions renderOptions,
        OutputPlan plan,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var svg = SvgRenderer.Render(session, renderOptions);
        using var workspace = new TempWorkspace();
        var tempSvgPath = workspace.GetFilePath("output.svg");
        await File.WriteAllTextAsync(
                tempSvgPath,
                svg,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (plan.Format == OutputFormat.Png)
        {
            await ConvertSvgToPngAsync(tempSvgPath, outputPath, logger, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var tempPngPath = workspace.GetFilePath("output.png");
        await ConvertSvgToPngAsync(tempSvgPath, tempPngPath, logger, cancellationToken)
            .ConfigureAwait(false);
        await ConvertPngToFinalAsync(tempPngPath, outputPath, plan.Format, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EmitAnimatedRasterAsync(
        RecordingSession session,
        AppOptions options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var plan = OutputPlan.Create(options);
        using var workspace = new TempWorkspace();
        var svgFramesDir = string.IsNullOrWhiteSpace(options.SaveFramesDir)
            ? workspace.CreateDirectory("svg-frames")
            : options.SaveFramesDir!;

        await SvgFrameExporter.SaveFramesAsync(
                session,
                SvgRenderOptions.FromAppOptions(options),
                svgFramesDir,
                options.VideoFps,
                logger,
                cancellationToken,
                announce: !string.IsNullOrWhiteSpace(options.SaveFramesDir)
            )
            .ConfigureAwait(false);

        var pngFramesDir = workspace.CreateDirectory("png-frames");
        await ConvertSvgFramesToPngAsync(svgFramesDir, pngFramesDir, logger, cancellationToken)
            .ConfigureAwait(false);
        await ConvertPngSequenceToFinalAsync(
                pngFramesDir,
                options.OutputPath,
                plan.Format,
                options.VideoFps,
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task ConvertSvgToPngAsync(
        string inputSvgPath,
        string outputPngPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var resvg = ToolResolver.ResolveResvg();
        await ExternalToolRunner.RunAsync(
                resvg,
                new[] { inputSvgPath, outputPngPath },
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task ConvertSvgFramesToPngAsync(
        string svgFramesDir,
        string pngFramesDir,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var framePaths = Directory
            .EnumerateFiles(svgFramesDir, "frame-*.svg")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        if (framePaths.Length == 0)
        {
            throw new InvalidOperationException("No SVG frames were generated for conversion.");
        }

        foreach (var framePath in framePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputFileName = Path.GetFileNameWithoutExtension(framePath) + ".png";
            var outputPath = Path.Combine(pngFramesDir, outputFileName);
            await ConvertSvgToPngAsync(framePath, outputPath, logger, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ConvertPngToFinalAsync(
        string inputPngPath,
        string outputPath,
        OutputFormat format,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var ffmpeg = ToolResolver.ResolveFfmpeg();
        var arguments = new System.Collections.Generic.List<string> { "-y", "-i", inputPngPath };

        if (format == OutputFormat.Jpeg)
        {
            arguments.AddRange(new[] { "-frames:v", "1" });
        }

        if (format == OutputFormat.Webp)
        {
            arguments.AddRange(new[] { "-frames:v", "1" });
        }

        arguments.Add(outputPath);
        await ExternalToolRunner.RunAsync(ffmpeg, arguments, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ConvertPngSequenceToFinalAsync(
        string pngFramesDir,
        string outputPath,
        OutputFormat format,
        double fps,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var ffmpeg = ToolResolver.ResolveFfmpeg();
        var inputPattern = Path.Combine(pngFramesDir, "frame-%04d.png");
        var arguments = new System.Collections.Generic.List<string>
        {
            "-y",
            "-framerate",
            fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i",
            inputPattern,
        };

        if (format == OutputFormat.Mp4)
        {
            arguments.AddRange(
                new[]
                {
                    "-c:v",
                    "mpeg4",
                    "-pix_fmt",
                    "yuv420p",
                    "-vf",
                    "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                }
            );
        }

        arguments.Add(outputPath);
        await ExternalToolRunner.RunAsync(ffmpeg, arguments, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
