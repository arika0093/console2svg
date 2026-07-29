using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Svg;

public static partial class SvgConverter
{
    private static async Task ConvertSvgToPngAsync(
        string svgPath,
        string pngPath,
        SvgConverterMode converter,
        double? width,
        double? height,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        EnsureDirectoryFor(pngPath);

        if (converter == SvgConverterMode.RsvgConvert)
        {
            await ConvertSvgToPngViaRsvgAsync(
                    svgPath,
                    pngPath,
                    width,
                    height,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        if (converter == SvgConverterMode.Resvg)
        {
            await ConvertSvgToPngViaResvgAsync(
                    svgPath,
                    pngPath,
                    width,
                    height,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(
            $"Converter {converter} cannot directly produce PNG (use ffmpeg for SVG-capable builds)."
        );
    }

    /// <summary>
    /// Runs <c>rsvg-convert</c> to render an SVG to PNG. Width/height, when
    /// given, are forwarded; otherwise the SVG's intrinsic size is used.
    /// </summary>
    private static async Task ConvertSvgToPngViaRsvgAsync(
        string svgPath,
        string pngPath,
        double? width,
        double? height,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var exe = FindRsvgConvertExecutable();
        var args = new List<string>();
        if (width.HasValue)
        {
            args.AddRange(["-w", FormatPx(width.Value)]);
        }
        if (height.HasValue)
        {
            args.AddRange(["-h", FormatPx(height.Value)]);
        }
        args.Add(svgPath);
        args.AddRange(["-o", pngPath]);

        logger.ZLogDebug($"Running rsvg-convert: {exe} {string.Join(' ', args)}");

        using var process = new Process();
        process.StartInfo.FileName = exe;
        process.StartInfo.UseShellExecute = false;
        foreach (var a in args)
        {
            process.StartInfo.ArgumentList.Add(a);
        }

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start rsvg-convert. Install 'librsvg2-bin' (Debian/Ubuntu) "
                    + "or 'librsvg' (Homebrew).\n"
                    + ex.Message,
                ex
            );
        }

        using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            { /* process may have already exited */
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"rsvg-convert exited with code {process.ExitCode}."
            );
        }
    }

    /// <summary>
    /// Renders an SVG to PNG using the bundled resvg library. The native
    /// library is loaded lazily; if it's missing, an informative error is
    /// thrown instead of crashing at startup.
    /// </summary>
    private static async Task ConvertSvgToPngViaResvgAsync(
        string svgPath,
        string pngPath,
        double? width,
        double? height,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        // The native renderer's API is synchronous and CPU-bound. Run it on a thread
        // pool thread so the caller's async flow can observe the cancellation.
        var svg = await File.ReadAllTextAsync(svgPath, cancellationToken).ConfigureAwait(false);

        logger.ZLogDebug($"Rendering SVG ({svg.Length} chars) via bundled resvg → {pngPath}");

        await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] pngBytes;
                    try
                    {
                        pngBytes = ResvgNative.RenderToPng(
                            svg,
                            width.HasValue ? ToPxInt(width.Value) : null,
                            height.HasValue ? ToPxInt(height.Value) : null
                        );
                    }
                    catch (Exception ex) when (IsResvgLoadFailure(ex))
                    {
                        throw new InvalidOperationException(
                            "Bundled resvg native library failed to load. Falling back "
                                + "requires the resvg native runtime shipped with this build.",
                            ex
                        );
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureDirectoryFor(pngPath);
                    File.WriteAllBytes(pngPath, pngBytes);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Detects and warms the bundled resvg renderer. Loading system fonts here
    /// means all subsequent renders reuse the same native font database.
    /// </summary>
    private static bool DetectResvg()
    {
        try
        {
            ResvgNative.WarmSystemFonts();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true when an exception looks like a native-lib load failure.</summary>
    private static bool IsResvgLoadFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is DllNotFoundException || e is TypeLoadException)
            {
                return true;
            }

            var msg = e.Message ?? string.Empty;
            if (
                msg.Contains("libresvg", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Cannot find", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("shared object", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("DllNotFoundException", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Throws if ffmpeg exits non-zero. Describes the invocation in debug logs.</summary>
    private static async Task RunFfmpegAsync(
        string ffmpegPath,
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        logger.ZLogDebug($"Running ffmpeg: {ffmpegPath} {string.Join(' ', args)}");

        using var process = new Process { StartInfo = CreateFfmpegStartInfo(ffmpegPath, args) };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to start ffmpeg. Please ensure ffmpeg is installed "
                    + "(bundled with the application or available in PATH).\n"
                    + ex.Message,
                ex
            );
        }

        // ffmpeg's progress and diagnostics must not be written into the interactive
        // terminal. Start draining both streams immediately to avoid blocking when a
        // conversion produces enough output to fill an OS pipe buffer.
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            { /* process may have already exited */
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}. "
                    + "Ensure ffmpeg supports the requested output format "
                    + "(SVG input requires the librsvg input device)."
                    + FormatFfmpegError(standardError)
            );
        }

        logger.ZLogDebug($"ffmpeg completed successfully.");
    }

    private static ProcessStartInfo CreateFfmpegStartInfo(string ffmpegPath, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static string FormatFfmpegError(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return string.Empty;
        }

        const int maxLength = 2_000;
        var details = standardError.Trim();
        if (details.Length > maxLength)
        {
            details = details[^maxLength..];
        }

        return $"\nffmpeg stderr:\n{details}";
    }
}
