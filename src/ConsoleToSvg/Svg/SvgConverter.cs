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
using ResvgSharp;
using ZLogger;

namespace ConsoleToSvg.Svg;

/// <summary>Selection mode for the SVG → raster converter.</summary>
public enum SvgConverterMode
{
    /// <summary>Auto-detect: prefer ffmpeg+librsvg, then rsvg-convert, then ResvgSharp.</summary>
    Auto,

    /// <summary>Force ffmpeg (requires librsvg). Fails if ffmpeg can't read SVG.</summary>
    Ffmpeg,

    /// <summary>Force the rsvg-convert CLI tool.</summary>
    RsvgConvert,

    /// <summary>Force the managed ResvgSharp library.</summary>
    Resvg,
}

/// <summary>
/// Handles detection of available SVG-to-PNG converters and performs the
/// SVG → raster pipeline, falling back from ffmpeg (librsvg) to rsvg-convert
/// or ResvgSharp when ffmpeg cannot read SVG directly. See issue #43.
/// </summary>
internal static class SvgConverter
{
    // Reuse ffmpeg discovery from Program. We pass the ffmpeg path in to avoid
    // duplicating logic; detection helpers here only need to know whether the
    // binary exists and what it supports.

    private static readonly Lazy<bool> _rsvgConvertAvailable = new(
        () => !string.IsNullOrEmpty(FindRsvgConvertExecutable())
    );

    private static readonly Lazy<bool> _resvgAvailable = new(DetectResvg);

    // Resolved bundled ffmpeg path set by Program.Main (which checks the
    // bundled layout next to the binary). When null/empty, detection falls
    // back to the bundled dir and PATH.
    private static string? _resolvedFfmpegPath;

    private static readonly Lazy<bool> _ffmpegAvailable = new(
        () => !string.IsNullOrEmpty(FindFfmpegForDetection())
    );

    // Cached so we don't shell out to ffmpeg on every call.
    private static readonly Lazy<bool> _ffmpegSupportsSvg = new(CheckFfmpegSvgSupport);

    /// <summary>
    /// Resolves the converter to use given the user's preference and the
    /// local environment. Throws <see cref="InvalidOperationException"/> when
    /// the forced converter is unavailable.
    /// </summary>
    public static SvgConverterMode ResolveConverter(SvgConverterMode wanted, bool ffmpegAvailableOverride, ILogger logger)
    {
        if (wanted == SvgConverterMode.Ffmpeg)
        {
            if (!ffmpegAvailableOverride && !_ffmpegAvailable.Value)
            {
                throw new InvalidOperationException(
                    "--svg-converter ffmpeg was requested but ffmpeg is not installed or not on PATH."
                );
            }
            if (!_ffmpegSupportsSvg.Value)
            {
                logger.ZLogDebug(
                    $"ffmpeg forced but lacks librsvg; SVG input will likely fail."
                );
            }
            return SvgConverterMode.Ffmpeg;
        }

        if (wanted == SvgConverterMode.RsvgConvert)
        {
            if (!_rsvgConvertAvailable.Value)
            {
                throw new InvalidOperationException(
                    "--svg-converter rsvg-convert was requested but rsvg-convert is not installed "
                    + "(install 'librsvg2-bin' on Debian/Ubuntu or 'librsvg' via Homebrew)."
                );
            }
            return SvgConverterMode.RsvgConvert;
        }

        if (wanted == SvgConverterMode.Resvg)
        {
            if (!_resvgAvailable.Value)
            {
                throw new InvalidOperationException(
                    "--svg-converter resvg was requested but the ResvgSharp native library is not available."
                );
            }
            return SvgConverterMode.Resvg;
        }

        // Auto: prefer ffmpeg+librsvg, then rsvg-convert, then ResvgSharp.
        // When ffmpeg is present but lacks SVG support, prefer the fallback
        // converters so that PNG output works (ffmpeg can still be used for
        // PNG → JPG/MP4 onwards).
        if (_ffmpegAvailable.Value && _ffmpegSupportsSvg.Value)
        {
            logger.ZLogDebug($"Auto-selected ffmpeg (librsvg) for SVG conversion.");
            return SvgConverterMode.Ffmpeg;
        }

        if (_rsvgConvertAvailable.Value)
        {
            logger.ZLogDebug($"Auto-selected rsvg-convert for SVG conversion.");
            return SvgConverterMode.RsvgConvert;
        }

        if (_resvgAvailable.Value)
        {
            logger.ZLogDebug($"Auto-selected ResvgSharp for SVG conversion.");
            return SvgConverterMode.Resvg;
        }

        // Last resort: ffmpeg is available but can't read SVG. We still return
        // Ffmpeg so that the call surfaces a clear error, instead of silently
        // doing nothing.
        if (_ffmpegAvailable.Value)
        {
            logger.ZLogDebug(
                $"No SVG-capable fallback found; falling through to ffmpeg (will likely fail)."
            );
            return SvgConverterMode.Ffmpeg;
        }

        throw new InvalidOperationException(
            "No SVG-to-PNG converter available. Install ffmpeg (with librsvg), "
            + "rsvg-convert ('librsvg2-bin' / 'brew install librsvg'), or build "
            + "with the ResvgSharp native runtime."
        );
    }

    /// <summary>
    /// Converts a single SVG file to a raster image (png/jpg/…).
    /// When a fallback converter is used, SVG → PNG is rendered first, then
    /// ffmpeg (if needed) takes PNG → final format.
    /// </summary>
    public static async Task ConvertSvgToImageAsync(
        string svgPath,
        string outputPath,
        SvgConverterMode converter,
        string ffmpegPath,
        double? width,
        double? height,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var outputExt = Path.GetExtension(outputPath)
            .TrimStart('.')
            .ToLowerInvariant();
        var isPng = string.Equals(outputExt, "png", StringComparison.Ordinal);

        // When ffmpeg (librsvg) handles SVG, just delegate to the legacy path.
        if (converter == SvgConverterMode.Ffmpeg)
        {
            await RunFfmpegAsync(
                    ffmpegPath,
                    ["-y", "-i", svgPath, "-frames:v", "1", "-update", "1", outputPath],
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        // Fallback path: SVG → PNG → (optionally) final format.
        // Render to a temp PNG, then pipe to ffmpeg if the target isn't PNG.
        var tempPng = isPng
            ? outputPath
            : Path.Combine(
                Path.GetTempPath(),
                $"c2s-{Guid.NewGuid():N}.png"
            );

        try
        {
            await ConvertSvgToPngAsync(
                    svgPath,
                    tempPng,
                    converter,
                    width,
                    height,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (isPng)
            {
                // PNG output: ffmpeg not needed.
                logger.ZLogDebug($"PNG written via fallback converter: {tempPng}");
                return;
            }

            // PNG → final format via ffmpeg. -frames:v 1 -update 1 avoid the
            // "image sequence pattern" warning for single-frame outputs.
            await RunFfmpegAsync(
                    ffmpegPath,
                    ["-y", "-i", tempPng, "-frames:v", "1", "-update", "1", outputPath],
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            if (!isPng && File.Exists(tempPng))
            {
                try { File.Delete(tempPng); }
                catch (Exception ex)
                {
                    logger.ZLogDebug(ex, $"Failed to delete temp PNG {tempPng}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Converts a directory of <c>frame-NNNN.svg</c> files into a video.
    /// When ffmpeg can't read SVG, frames are first transcoded to PNG via
    /// the fallback converter, then ffmpeg reads the PNG sequence.
    /// </summary>
    public static async Task ConvertFramesToVideoAsync(
        string framesDir,
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
        string framePattern;

        if (converter == SvgConverterMode.Ffmpeg)
        {
            framePattern = Path.Combine(framesDir, "frame-%04d.svg");
        }
        else
        {
            // Pre-convert all SVG frames to PNG so ffmpeg can ingest them.
            var svgFiles = Directory.EnumerateFiles(framesDir, "frame-*.svg")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            if (svgFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No SVG frames found in {framesDir} for video conversion."
                );
            }

            logger.ZLogDebug(
                $"Pre-converting {svgFiles.Count} SVG frame(s) to PNG via {converter}."
            );

            var parallelOpts = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                // Keep CPU/native-library contention bounded.
                MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount),
            };

            await Parallel
                .ForEachAsync(
                    svgFiles,
                    parallelOpts,
                    async (svgFile, ct) =>
                    {
                        var pngFile = Path.Combine(
                            framesDir,
                            Path.GetFileNameWithoutExtension(svgFile) + ".png"
                        );
                        await ConvertSvgToPngAsync(
                                svgFile,
                                pngFile,
                                converter,
                                width,
                                height,
                                logger,
                                ct
                            )
                            .ConfigureAwait(false);

                        // Remove the SVG so ffmpeg's pattern doesn't pick it up.
                        try { File.Delete(svgFile); }
                        catch (Exception ex)
                        {
                            logger.ZLogDebug(
                                ex,
                                $"Failed to delete frame SVG {svgFile}: {ex.Message}"
                            );
                        }
                    }
                )
                .ConfigureAwait(false);

            framePattern = Path.Combine(framesDir, "frame-%04d.png");
        }

        var fpsStr = fps.ToString(CultureInfo.InvariantCulture);
        await RunFfmpegAsync(
                ffmpegPath,
                ["-y", "-framerate", fpsStr, "-i", framePattern, outputPath],
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Renders a single SVG file to a PNG file using the chosen fallback.</summary>
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
        var exe = FindRsvgConvertExecutable()!;
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
            try { process.Kill(entireProcessTree: true); }
            catch { /* process may have already exited */ }
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
    /// Renders an SVG to PNG using the managed ResvgSharp library. The native
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
        // ResvgSharp's API is synchronous and CPU-bound. Run it on a thread
        // pool thread so the caller's async flow can observe the cancellation.
        var svg = await File.ReadAllTextAsync(svgPath, cancellationToken)
            .ConfigureAwait(false);

        logger.ZLogDebug(
            $"Rendering SVG ({svg.Length} chars) via ResvgSharp → {pngPath}"
        );

        await Task
            .Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var options = new ResvgOptions
                    {
                        // Render the entire SVG viewport.
                        ExportAreaPage = true,
                    };
                    if (width.HasValue)
                    {
                        options.Width = ToPxInt(width.Value);
                    }
                    if (height.HasValue)
                    {
                        options.Height = ToPxInt(height.Value);
                    }

                    byte[] pngBytes;
                    try
                    {
                        pngBytes = Resvg.RenderToPng(svg, options);
                    }
                    catch (Exception ex)
                        when (IsResvgLoadFailure(ex))
                    {
                        throw new InvalidOperationException(
                            "ResvgSharp native library failed to load. Falling back "
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
    /// Detects whether the ResvgSharp managed wrapper can load its native
    /// counterpart. We do a tiny throwaway render rather than probing for the
    /// native lib on disk so the check works portably across RIDs.
    /// </summary>
    private static bool DetectResvg()
    {
        try
        {
            var tiny = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>";
            _ = Resvg.RenderToPng(tiny);
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

        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.UseShellExecute = false;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

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

        using var killOnCancel = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* process may have already exited */ }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}. "
                + "Ensure ffmpeg supports the requested output format "
                + "(SVG input requires the librsvg input device)."
            );
        }

        logger.ZLogDebug($"ffmpeg completed successfully.");
    }

    /// <summary>
    /// Sets the resolved bundled ffmpeg path so that lazy auto-detection in
    /// <see cref="SvgConverter"/> finds ffmpeg even when not on PATH.
    /// Called by Program.Main (which uses FindFfmpegExecutable that checks
    /// the bundled layout next to the binary AND PATH).
    /// </summary>
    public static void SetFfmpegPath(string path)
    {
        _resolvedFfmpegPath = string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// Finds the ffmpeg binary for support detection.
    /// Prefers the path resolved by Program.Main, then checks the bundled
    /// layout next to this binary, then falls back to PATH.
    /// </summary>
    private static string FindFfmpegForDetection()
    {
        if (!string.IsNullOrEmpty(_resolvedFfmpegPath) && File.Exists(_resolvedFfmpegPath))
        {
            return _resolvedFfmpegPath;
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ffmpeg.exe"
            : "ffmpeg";

        // Check next to this binary (bundled / npm layout)
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var bundled = Path.Combine(exeDir, exeName);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        // PATH
        return FindExecutable(exeName);
    }

    /// <summary>
    /// Probes whether ffmpeg can actually decode SVG (rasterize via librsvg)
    /// by doing a minimal SVG → PNG test conversion. <c>ffmpeg -formats</c>
    /// lists <c>svg_pipe</c> even when librsvg decoder is NOT enabled
    /// (false positive), so only a real conversion confirms support.
    /// Result is cached via <see cref="_ffmpegSupportsSvg"/>.
    /// </summary>
    private static bool CheckFfmpegSvgSupport()
    {
        var exe = FindFfmpegForDetection();
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        var tempDir = Path.GetTempPath();
        var tempSvg = Path.Combine(tempDir, $"c2s-probe-{Guid.NewGuid():N}.svg");
        var tempPng = Path.Combine(tempDir, $"c2s-probe-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllText(tempSvg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>");

            using var process = new Process();
            process.StartInfo.FileName = exe;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(tempSvg);
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-frames:v");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add("-update");
            process.StartInfo.ArgumentList.Add("1");
            process.StartInfo.ArgumentList.Add(tempPng);

            process.Start();
            process.WaitForExit();

            return process.ExitCode == 0 && File.Exists(tempPng);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(tempSvg)) File.Delete(tempSvg); } catch { }
            try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
        }
    }

    /// <summary>Finds rsvg-convert (or rsvg-convert.exe) on PATH.</summary>
    private static string FindRsvgConvertExecutable()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "rsvg-convert.exe"
            : "rsvg-convert";

        // 1. next to this binary (bundled layout)
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrEmpty(exeDir))
        {
            var bundled = Path.Combine(exeDir, exeName);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        // 2. PATH
        return FindExecutable(exeName);
    }

    /// <summary>
    /// Resolves a binary name to its full path using <c>which</c> semantics
    /// across platforms. Returns an empty string when not found.
    /// </summary>
    private static string FindExecutable(string name)
    {
        if (File.Exists(name))
        {
            return Path.GetFullPath(name);
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return string.Empty;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return string.Empty;
    }

    private static void EnsureDirectoryFor(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string FormatPx(double px) =>
        Math.Round(px, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    private static int ToPxInt(double px) => (int)Math.Round(px, MidpointRounding.AwayFromZero);
}