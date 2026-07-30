using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Svg;

public static partial class SvgConverter
{
    /// <summary>
    /// Encodes SVG frames without writing frame files. SVG-capable ffmpeg builds
    /// receive SVG directly; other converters render each frame to PNG bytes and
    /// feed an image2pipe PNG stream to ffmpeg.
    /// </summary>
    public static async Task ConvertSvgFramesToVideoAsync(
        IEnumerable<string> svgFrames,
        double fps,
        string outputPath,
        SvgConverterMode converter,
        string ffmpegPath,
        double? width,
        double? height,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var effectiveConverter = ResolvePreConversionConverter(converter);
        var codec = effectiveConverter == SvgConverterMode.Ffmpeg ? "svg" : "png";
        var args = CreateInMemoryVideoFfmpegArgs(fps, codec, outputPath);

        logger.ZLogDebug($"Encoding video from in-memory {codec.ToUpperInvariant()} frames.");
        using var process = new Process { StartInfo = CreateFfmpegStartInfo(ffmpegPath, args) };
        process.StartInfo.RedirectStandardInput = true;
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to start ffmpeg.\n" + ex.Message, ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ffmpeg may already have exited between cancellation and Kill.
            }
        });

        try
        {
            foreach (var svg in svgFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = effectiveConverter == SvgConverterMode.Ffmpeg
                    ? Encoding.UTF8.GetBytes(svg)
                    : await RenderSvgToPngAsync(svg, effectiveConverter, width, height, logger, cancellationToken)
                        .ConfigureAwait(false);
                await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            await process.StandardInput.DisposeAsync().ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the original write/render failure when ffmpeg is already gone.
            }
            throw;
        }

        await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}. "
                    + "Ensure ffmpeg supports the requested output format."
                    + FormatFfmpegError(standardError)
            );
        }
    }

    private static string[] CreateInMemoryVideoFfmpegArgs(
        double fps,
        string codec,
        string outputPath
    ) =>
    [
        "-y",
        "-framerate",
        fps.ToString(CultureInfo.InvariantCulture),
        "-f",
        "image2pipe",
        "-vcodec",
        codec,
        "-i",
        "pipe:0",
        outputPath,
    ];
}
