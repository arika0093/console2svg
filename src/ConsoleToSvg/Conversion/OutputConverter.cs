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

namespace ConsoleToSvg.Conversion;

internal enum RasterConversionStrategy
{
    DirectSvgWithFfmpeg,
    ResvgPngOnly,
    ResvgThenFfmpeg,
}

internal static class OutputConverter
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "webm", "avi", "mov", "mkv", "ogv", "flv", "ts", "wmv", "m4v", "gif",
    };

    internal static bool IsVideoFormat(string extension) => VideoExtensions.Contains(extension);

    internal static string GetExecutableFileName(string toolName, bool isWindows) =>
        isWindows ? $"{toolName}.exe" : toolName;

    internal static string? TryResolveExecutable(string toolName) =>
        TryResolveExecutable(
            toolName,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Environment.ProcessPath,
            Environment.GetEnvironmentVariable("PATH")
        );

    internal static string? TryResolveExecutable(
        string toolName,
        bool isWindows,
        string? processPath,
        string? pathEnvironment
    )
    {
        var fileName = GetExecutableFileName(toolName, isWindows);
        var bundled = TryResolveBundledExecutable(fileName, processPath);
        if (bundled is not null)
        {
            return bundled;
        }

        if (string.IsNullOrWhiteSpace(pathEnvironment))
        {
            return null;
        }

        foreach (var rawDirectory in pathEnvironment.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                 ))
        {
            var directory = rawDirectory.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static RasterConversionStrategy GetRasterConversionStrategy(
        string outputPath,
        bool resvgAvailable
    )
    {
        var outputExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        if (outputExtension == "png")
        {
            return resvgAvailable
                ? RasterConversionStrategy.ResvgPngOnly
                : RasterConversionStrategy.DirectSvgWithFfmpeg;
        }

        return resvgAvailable
            ? RasterConversionStrategy.ResvgThenFfmpeg
            : RasterConversionStrategy.DirectSvgWithFfmpeg;
    }

    internal static string GetVideoFrameExtension(bool resvgAvailable) =>
        resvgAvailable ? "png" : "svg";

    internal static async Task ConvertSvgToRasterAsync(
        string svgPath,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var resvg = TryResolveExecutable("resvg");
        switch (GetRasterConversionStrategy(outputPath, resvg is not null))
        {
            case RasterConversionStrategy.ResvgPngOnly:
                logger.ZLogDebug($"Raster output via resvg only. Out={outputPath}");
                await RunResvgAsync(resvg!, svgPath, outputPath, logger, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case RasterConversionStrategy.ResvgThenFfmpeg:
                logger.ZLogDebug($"Raster output via resvg + ffmpeg. Out={outputPath}");
                var tempPng = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}.png");
                try
                {
                    await RunResvgAsync(resvg!, svgPath, tempPng, logger, cancellationToken)
                        .ConfigureAwait(false);
                    await RunFfmpegImageAsync(tempPng, outputPath, logger, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteFile(tempPng, logger);
                }
                return;

            default:
                logger.ZLogDebug(
                    $"resvg not found. Falling back to direct ffmpeg SVG conversion. Out={outputPath}"
                );
                await RunFfmpegImageAsync(
                        svgPath,
                        outputPath,
                        logger,
                        cancellationToken,
                        "Ensure ffmpeg supports SVG input or install resvg."
                    )
                    .ConfigureAwait(false);
                return;
        }
    }

    internal static async Task ConvertSvgFramesToVideoAsync(
        string framesDir,
        double fps,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var resvg = TryResolveExecutable("resvg");
        if (resvg is null)
        {
            logger.ZLogDebug($"resvg not found. Falling back to ffmpeg SVG frame input.");
            await RunFfmpegVideoAsync(
                    framesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(resvgAvailable: false),
                    logger,
                    cancellationToken,
                    "Ensure ffmpeg supports SVG input or install resvg."
                )
                .ConfigureAwait(false);
            return;
        }

        logger.ZLogDebug($"Video output via resvg + ffmpeg. Out={outputPath}");
        var pngFramesDir = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}");
        try
        {
            await ConvertSvgFramesToPngAsync(
                    resvg,
                    framesDir,
                    pngFramesDir,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await RunFfmpegVideoAsync(
                    pngFramesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(resvgAvailable: true),
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(pngFramesDir, logger);
        }
    }

    private static string? TryResolveBundledExecutable(string fileName, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var processDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(processDirectory))
        {
            return null;
        }

        var bundledCandidate = Path.Combine(processDirectory, fileName);
        return File.Exists(bundledCandidate) ? bundledCandidate : null;
    }

    private static string FindExecutableOrThrow(string toolName, string requiredMessage) =>
        TryResolveExecutable(toolName) ?? throw new InvalidOperationException(requiredMessage);

    private static Task RunResvgAsync(
        string resvg,
        string svgPath,
        string pngPath,
        ILogger logger,
        CancellationToken cancellationToken
    ) =>
        RunProcessAsync(
            toolDisplayName: "resvg",
            executablePath: resvg,
            args: [svgPath, pngPath],
            logger,
            cancellationToken,
            startFailureMessage:
                "Please ensure resvg is installed (bundled with the application or available in PATH).",
            exitFailureMessage: "Please ensure resvg can read the SVG input."
        );

    private static Task RunFfmpegImageAsync(
        string inputPath,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage = "Ensure ffmpeg supports the requested output format."
    ) =>
        RunFfmpegAsync(
            ["-y", "-i", inputPath, "-frames:v", "1", "-update", "1", outputPath],
            logger,
            cancellationToken,
            exitFailureMessage
        );

    private static Task RunFfmpegVideoAsync(
        string framesDir,
        double fps,
        string outputPath,
        string frameExtension,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage = "Ensure ffmpeg supports the requested output format."
    )
    {
        var framePattern = Path.Combine(framesDir, $"frame-%04d.{frameExtension}");
        var fpsValue = fps.ToString(CultureInfo.InvariantCulture);
        return RunFfmpegAsync(
            ["-y", "-framerate", fpsValue, "-i", framePattern, outputPath],
            logger,
            cancellationToken,
            exitFailureMessage
        );
    }

    private static Task RunFfmpegAsync(
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage
    )
    {
        var ffmpeg = FindExecutableOrThrow(
            "ffmpeg",
            "ffmpeg is required for this output format. Please ensure ffmpeg is installed "
                + "(bundled with the application or available in PATH)."
        );
        return RunProcessAsync(
            toolDisplayName: "ffmpeg",
            executablePath: ffmpeg,
            args,
            logger,
            cancellationToken,
            startFailureMessage:
                "Please ensure ffmpeg is installed (bundled with the application or available in PATH).",
            exitFailureMessage
        );
    }

    private static async Task ConvertSvgFramesToPngAsync(
        string resvg,
        string svgFramesDir,
        string pngFramesDir,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(pngFramesDir);
        foreach (var svgFramePath in Directory
                     .EnumerateFiles(svgFramesDir, "frame-*.svg")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pngFramePath = Path.Combine(
                pngFramesDir,
                $"{Path.GetFileNameWithoutExtension(svgFramePath)}.png"
            );
            await RunResvgAsync(resvg, svgFramePath, pngFramePath, logger, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RunProcessAsync(
        string toolDisplayName,
        string executablePath,
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken,
        string startFailureMessage,
        string exitFailureMessage
    )
    {
        logger.ZLogDebug($"Running {toolDisplayName}: {executablePath} {string.Join(' ', args)}");

        using var process = new Process();
        process.StartInfo.FileName = executablePath;
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
                $"Failed to start {toolDisplayName}. {startFailureMessage}\n{ex.Message}",
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
            {
                // Process may have already exited.
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{toolDisplayName} exited with code {process.ExitCode}. {exitFailureMessage}"
            );
        }

        logger.ZLogDebug($"{toolDisplayName} completed successfully.");
    }

    private static void TryDeleteFile(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Failed to delete temp file {path}: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path, ILogger logger)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Failed to delete temp dir {path}: {ex.Message}");
        }
    }
}
