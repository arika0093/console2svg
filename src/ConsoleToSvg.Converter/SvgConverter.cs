using System;
using System.Collections.Concurrent;
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

/// <summary>Selection mode for the SVG → raster converter.</summary>
public enum SvgConverterMode
{
    /// <summary>Auto-detect: prefer the bundled resvg host, then ffmpeg+librsvg.</summary>
    Auto,

    /// <summary>Force ffmpeg (requires librsvg). Fails if ffmpeg can't read SVG.</summary>
    Ffmpeg,

    /// <summary>Force the rsvg-convert CLI tool.</summary>
    RsvgConvert,

    /// <summary>Force the bundled resvg native renderer.</summary>
    Resvg,
}

/// <summary>
/// Handles detection of available SVG-to-PNG converters and performs the
/// SVG → raster pipeline, falling back from ffmpeg (librsvg) to the bundled
/// resvg host when ffmpeg cannot read SVG directly. See issue #43.
/// </summary>
public static partial class SvgConverter
{
    // Reuse ffmpeg discovery from Program. We pass the ffmpeg path in to avoid
    // duplicating logic; detection helpers here only need to know whether the
    // binary exists and what it supports.

    private static readonly Lazy<bool> _rsvgConvertAvailable = new(() =>
        !string.IsNullOrEmpty(FindRsvgConvertExecutable())
    );

    private static readonly Lazy<bool> _resvgAvailable = new(DetectResvg);

    // Resolved bundled ffmpeg path set by Program.Main (which checks the
    // bundled layout next to the binary). When null/empty, detection falls
    // back to the bundled dir and PATH.
    private static string? _resolvedFfmpegPath;

    // H.264 and yuv420p require even frame dimensions; a rendered SVG can have
    // odd pixel dimensions (rows × cell-height + chrome padding). Pad the frame
    // up to the next even size, anchoring content at the top-left so only the
    // right/bottom edges grow by at most 1px (issue #112).
    private const string VideoEvenDimensionFilter = "pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0";

    private static readonly Lazy<bool> _ffmpegAvailable = new(() =>
        !string.IsNullOrEmpty(FindFfmpegForDetection())
    );

    // Cached so we don't shell out to ffmpeg on every call.
    private static readonly Lazy<bool> _ffmpegSupportsSvg = new(CheckFfmpegSvgSupport);

    private static readonly ConcurrentDictionary<string, Lazy<VideoCodecAvailability>> _videoCodecAvailabilityByFfmpegPath = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// True when an ffmpeg binary was discovered (via <see cref="SetFfmpegPath"/>,
    /// the bundled layout, or PATH). Safe to call before a recording to drive
    /// the pre-recording tool check (issue #78).
    /// </summary>
    public static bool IsFfmpegAvailable => _ffmpegAvailable.Value;

    /// <summary>
    /// True when ffmpeg can actually decode SVG (librsvg input device enabled).
    /// Determined by a real SVG→PNG probe so we never rely on <c>-formats</c>
    /// listing (which can report false positives).
    /// </summary>
    public static bool FfmpegSupportsSvg => _ffmpegSupportsSvg.Value;

    /// <summary>
    /// Selects the best available H.264-compatible video codec for Windows Media Player.
    /// Prefers libx264 (H.264), falls back to mpeg4 (MPEG-4 Part 2), both supported by WMP.
    /// Returns null for non-MP4 formats (GIF, WebM) to let ffmpeg choose the appropriate encoder.
    /// </summary>
    internal static string? SelectVideoCodec(string outputPath, string ffmpegPath)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();

        // Only apply H.264 codec override for MP4 containers
        if (extension != ".mp4")
        {
            return null;
        }

        var executable = ResolveFfmpegExecutable(ffmpegPath);
        if (string.IsNullOrEmpty(executable))
        {
            throw new InvalidOperationException(
                "No compatible video codec found. ffmpeg must have libx264 or mpeg4 encoder. "
                    + "Install ffmpeg with libx264 support for best compatibility."
            );
        }

        var availableCodecs = _videoCodecAvailabilityByFfmpegPath
            .GetOrAdd(
                executable,
                static path => new Lazy<VideoCodecAvailability>(
                    () => DetectFfmpegVideoCodecs(path),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;

        if (availableCodecs.Libx264)
        {
            return "libx264";
        }

        if (availableCodecs.Mpeg4)
        {
            return "mpeg4";
        }

        throw new InvalidOperationException(
            "No compatible video codec found. ffmpeg must have libx264 or mpeg4 encoder. "
                + "Install ffmpeg with libx264 support for best compatibility."
        );
    }

    /// <summary>
    /// Resolves the converter to use given the user's preference and the
    /// local environment. Throws <see cref="InvalidOperationException"/> when
    /// the forced converter is unavailable.
    /// </summary>
    public static SvgConverterMode ResolveConverter(
        SvgConverterMode wanted,
        bool ffmpegAvailableOverride,
        ILogger logger
    )
    {
        if (wanted == SvgConverterMode.Ffmpeg)
        {
            if (!ffmpegAvailableOverride && !_ffmpegAvailable.Value)
            {
                throw new InvalidOperationException(
                    "--svg-converter ffmpeg was requested but ffmpeg is not installed or not on PATH. "
                        + "Install ffmpeg and ensure it is on PATH."
                );
            }
            if (!_ffmpegSupportsSvg.Value)
            {
                logger.ZLogDebug($"ffmpeg forced but lacks librsvg; SVG input will likely fail.");
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
                    "--svg-converter resvg was requested but the bundled resvg native library is not available."
                );
            }
            return SvgConverterMode.Resvg;
        }

        // Auto: prefer the bundled host so SVG → PNG never depends on the
        // installed ffmpeg build. Its process-wide font database is reused
        // for subsequent frames.
        if (_resvgAvailable.Value)
        {
            logger.ZLogDebug($"Auto-selected bundled resvg for SVG conversion.");
            return SvgConverterMode.Resvg;
        }

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
            "No SVG-to-PNG converter available. Install ffmpeg (with librsvg) "
                + "and ensure it is on PATH, "
                + "rsvg-convert ('librsvg2-bin' / 'brew install librsvg'), or build "
                + "with the bundled resvg native runtime."
        );
    }

    /// <summary>
    /// Given a resolved converter mode, returns the converter to use for the
    /// SVG → raster pre-conversion step. When the resolved converter is
    /// <see cref="SvgConverterMode.Ffmpeg"/> and ffmpeg can read SVG directly
    /// (librsvg enabled), returns <see cref="SvgConverterMode.Ffmpeg"/> — no
    /// pre-conversion is needed. When ffmpeg cannot decode SVG, resolves a
    /// fallback (rsvg-convert or bundled resvg) for the SVG → PNG step, then ffmpeg
    /// ingests PNGs for the final format. Throws when no SVG-capable converter
    /// is available at all (issue #79).
    /// </summary>
    private static SvgConverterMode ResolvePreConversionConverter(SvgConverterMode converter)
    {
        if (converter != SvgConverterMode.Ffmpeg)
        {
            return converter;
        }

        // ffmpeg handles SVG directly (librsvg enabled) — no pre-conversion needed.
        if (_ffmpegSupportsSvg.Value)
        {
            return converter;
        }

        // ffmpeg can't decode SVG; prefer the bundled host for the SVG → PNG
        // step so repeated conversions reuse its process-wide font database.
        if (_resvgAvailable.Value)
        {
            return SvgConverterMode.Resvg;
        }
        if (_rsvgConvertAvailable.Value)
        {
            return SvgConverterMode.RsvgConvert;
        }

        throw new InvalidOperationException(
            "ffmpeg cannot decode SVG (librsvg input device not enabled) and no fallback "
                + "converter (rsvg-convert, bundled resvg) is available. Install librsvg for ffmpeg, "
                + "rsvg-convert ('librsvg2-bin' on Debian/Ubuntu or 'librsvg' via Homebrew), or "
                + "use a build with the bundled resvg native runtime."
        );
    }

    /// <summary>
    /// Verifies that all tools required for the requested output format are
    /// available. Call this before starting a recording so that missing tools
    /// are reported immediately, without wasting a recording session (issue #78).
    /// Throws <see cref="InvalidOperationException"/> when a required tool is missing.
    /// </summary>
    /// <param name="wanted">User's converter preference (Auto / Ffmpeg / RsvgConvert / Resvg).</param>
    /// <param name="requiresFfmpeg">True when the output format (video or non-PNG image) needs ffmpeg for the final encode step.</param>
    /// <param name="logger">Logger for debug messages.</param>
    public static void VerifyConversionPipeline(
        SvgConverterMode wanted,
        bool requiresFfmpeg,
        ILogger logger
    )
    {
        // ResolveConverter throws when a forced converter is unavailable.
        var converter = ResolveConverter(wanted, _ffmpegAvailable.Value, logger);

        // ResolvePreConversionConverter throws when ffmpeg can't decode SVG
        // and no fallback converter (rsvg-convert, bundled resvg) is available.
        _ = ResolvePreConversionConverter(converter);

        // For video and non-PNG image output, ffmpeg is required for the final
        // encoding step (PNG → video / SVG → video when ffmpeg supports SVG).
        if (requiresFfmpeg && !_ffmpegAvailable.Value)
        {
            throw new InvalidOperationException(
                "ffmpeg is required for the requested output format but was not found. "
                    + "Install ffmpeg and ensure it is on PATH."
            );
        }
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
        IProgressReporter progressReporter,
        CancellationToken cancellationToken
    )
    {
        var outputExt = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        var isPng = string.Equals(outputExt, "png", StringComparison.Ordinal);

        // ResolvePreConversionConverter falls back to rsvg-convert/bundled resvg when
        // ffmpeg can't decode SVG (issue #79), so we never feed raw SVG to a
        // ffmpeg build that lacks the librsvg input device.
        var effectiveConverter = ResolvePreConversionConverter(converter);
        if (effectiveConverter == SvgConverterMode.Ffmpeg)
        {
            await progressReporter.ReportAsync("Converting SVG to image...", cancellationToken);
            await RunFfmpegAsync(
                    ffmpegPath,
                    ["-y", "-i", svgPath, "-frames:v", "1", "-update", "1", outputPath],
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await progressReporter.ReportAsync("Image conversion completed.", CancellationToken.None);
            return;
        }

        // Fallback path: SVG → PNG → (optionally) final format.
        // Render to a temp PNG, then pipe to ffmpeg if the target isn't PNG.
        var tempPng = isPng
            ? outputPath
            : Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}.png");

        try
        {
            await progressReporter.ReportAsync("Converting SVG to PNG...", cancellationToken);
            await ConvertSvgToPngAsync(
                    svgPath,
                    tempPng,
                    effectiveConverter,
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
                await progressReporter.ReportAsync("PNG conversion completed.", CancellationToken.None);
                return;
            }

            // PNG → final format via ffmpeg. -frames:v 1 -update 1 avoid the
            // "image sequence pattern" warning for single-frame outputs.
            await progressReporter.ReportAsync("Converting PNG to final format...", cancellationToken);
            await RunFfmpegAsync(
                    ffmpegPath,
                    ["-y", "-i", tempPng, "-frames:v", "1", "-update", "1", outputPath],
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await progressReporter.ReportAsync("Image conversion completed.", CancellationToken.None);
        }
        finally
        {
            if (!isPng && File.Exists(tempPng))
            {
                try
                {
                    File.Delete(tempPng);
                }
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
        IProgressReporter progressReporter,
        CancellationToken cancellationToken
    )
    {
        string framePattern;

        // Resolve the effective SVG → raster converter. When ffmpeg can read
        // SVG (librsvg enabled), frames are fed directly to ffmpeg. When
        // ffmpeg can't decode SVG, ResolvePreConversionConverter falls back to
        // rsvg-convert/bundled resvg for the SVG → PNG pre-conversion step, then
        // ffmpeg ingests PNGs for the final video encode (issue #79).
        var codec = SelectVideoCodec(outputPath, ffmpegPath);
        var effectiveConverter = ResolvePreConversionConverter(converter);

        if (effectiveConverter == SvgConverterMode.Ffmpeg)
        {
            framePattern = Path.Combine(framesDir, "frame-%04d.svg");
        }
        else
        {
            // Pre-convert all SVG frames to PNG so ffmpeg can ingest them.
            var svgFiles = Directory
                .EnumerateFiles(framesDir, "frame-*.svg")
                .ToArray();

            if (svgFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No SVG frames found in {framesDir} for video conversion."
                );
            }

            logger.ZLogDebug(
                $"Pre-converting {svgFiles.Length} SVG frame(s) to PNG via {effectiveConverter}."
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
                                effectiveConverter,
                                width,
                                height,
                                logger,
                                ct
                            )
                            .ConfigureAwait(false);

                        // Intermediate SVGs are left in place and removed
                        // together with the temp directory after ffmpeg runs
                        // (frame-%04d.png only matches PNGs, so ffmpeg is
                        // unaffected). Deleting per-frame on Windows triggers
                        // per-file antivirus scans that dramatically slow the
                        // pipeline (see issue about slow mp4 on Windows).
                    }
                )
                .ConfigureAwait(false);

            framePattern = Path.Combine(framesDir, "frame-%04d.png");
        }

        var fpsStr = fps.ToString(CultureInfo.InvariantCulture);
        string[] ffmpegArgs = codec is null
            ? ["-y", "-framerate", fpsStr, "-i", framePattern, "-pix_fmt", "yuv420p", "-vf", VideoEvenDimensionFilter, outputPath]
            : ["-y", "-framerate", fpsStr, "-i", framePattern, "-c:v", codec, "-pix_fmt", "yuv420p", "-vf", VideoEvenDimensionFilter, outputPath];

        await progressReporter.ReportAsync("Encoding video frames...", cancellationToken);
        await RunFfmpegAsync(
                ffmpegPath,
                ffmpegArgs,
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);
        await progressReporter.ReportAsync("Video encoding completed.", CancellationToken.None);
    }
}
