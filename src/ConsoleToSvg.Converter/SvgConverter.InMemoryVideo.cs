using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Svg;

public static partial class SvgConverter
{
    /// <summary>
    /// Encodes SVG frames without writing frame files. Each frame is rendered to
    /// PNG in memory and streamed to ffmpeg via image2pipe, avoiding intermediate
    /// files on disk.
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
        // The in-memory pipe path always renders to PNG because ffmpeg's
        // image2pipe demuxer cannot split concatenated SVG documents into
        // individual frames (unlike file-based frame-%04d.svg input).
        var effectiveConverter = ResolveInMemoryPngConverter(converter);
        var args = CreateInMemoryVideoFfmpegArgs(fps, outputPath);

        logger.ZLogDebug($"Encoding video from in-memory PNG frames.");
        await Console.Error.WriteLineAsync("Encoding video frames...".AsMemory(), cancellationToken);
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

        var frameCount = 0;
        try
        {
            foreach (var svg in svgFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await RenderSvgToPngAsync(svg, effectiveConverter, width, height, logger, cancellationToken)
                    .ConfigureAwait(false);
                await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }
            await process.StandardInput.DisposeAsync().ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Preserve the original write/render failure when ffmpeg is already gone.
            }
            var stderrOnFailure = await standardErrorTask.ConfigureAwait(false);
            throw new InvalidOperationException(
                "Failed while feeding frames to ffmpeg." + FormatFfmpegError(stderrOnFailure),
                ex
            );
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
        await Console.Error.WriteLineAsync($"Encoded {frameCount} frames to video.".AsMemory(), CancellationToken.None);
    }

    private static string[] CreateInMemoryVideoFfmpegArgs(
        double fps,
        string outputPath
    )
    {
        if (fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fps), fps, "Frame rate must be positive.");
        }

        var codec = SelectVideoCodec(outputPath);

        // For non-MP4 formats (GIF, WebM), let ffmpeg choose the appropriate encoder
        if (codec is null)
        {
            return
            [
                "-y",
                "-framerate",
                fps.ToString(CultureInfo.InvariantCulture),
                "-f",
                "image2pipe",
                "-vcodec",
                "png",
                "-i",
                "pipe:0",
                outputPath,
            ];
        }

        return
        [
            "-y",
            "-framerate",
            fps.ToString(CultureInfo.InvariantCulture),
            "-f",
            "image2pipe",
            "-vcodec",
            "png",
            "-i",
            "pipe:0",
            "-c:v",
            codec,
            "-pix_fmt",
            "yuv420p",
            outputPath,
        ];
    }

    /// <summary>
    /// Resolves a PNG-capable converter for the in-memory pipe path. Unlike
    /// file-based input (where ffmpeg can read individual SVG files), the
    /// image2pipe demuxer cannot split concatenated SVG documents, so the
    /// in-memory path always renders to PNG first.
    /// </summary>
    private static SvgConverterMode ResolveInMemoryPngConverter(SvgConverterMode converter)
    {
        if (converter != SvgConverterMode.Ffmpeg)
        {
            return converter;
        }

        if (_resvgAvailable.Value)
        {
            return SvgConverterMode.Resvg;
        }

        if (_rsvgConvertAvailable.Value)
        {
            return SvgConverterMode.RsvgConvert;
        }

        throw new InvalidOperationException(
            "In-memory video rendering requires resvg or rsvg-convert. "
                + "Install 'librsvg2-bin' (Debian/Ubuntu) or 'librsvg' (Homebrew)."
        );
    }
}
