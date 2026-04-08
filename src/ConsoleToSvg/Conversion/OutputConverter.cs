using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Raster;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Conversion;

internal enum RasterConversionStrategy
{
    DirectSvgWithFfmpeg,
    ResvgPngOnly,
    ResvgThenFfmpeg,
}

internal sealed class ConversionToolchain
{
    internal ConversionToolchain(string? ffmpegPath, bool ffmpegSupportsSvgInput)
    {
        FfmpegPath = ffmpegPath;
        FfmpegSupportsSvgInput = ffmpegSupportsSvgInput;
    }

    internal string? FfmpegPath { get; }

    internal bool FfmpegSupportsSvgInput { get; }

    internal bool HasFfmpeg => !string.IsNullOrWhiteSpace(FfmpegPath);
}

internal static class OutputConverter
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "webm", "avi", "mov", "mkv", "ogv", "flv", "ts", "wmv", "m4v", "gif",
    };

    private const string FfmpegInstallHint =
        "Install ffmpeg with SVG input support (librsvg-enabled build), or use a standard ffmpeg build together with the bundled ResvgSharp runtime assets.";

    internal static bool IsVideoFormat(string extension) => VideoExtensions.Contains(extension);

    internal static string GetExecutableFileName(string toolName, bool isWindows) =>
        isWindows ? $"{toolName}.exe" : toolName;

    internal static string? TryResolveExecutable(string toolName)
        => TryResolveExecutable(
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

        var bundled = TryResolveDirectoryExecutable(fileName, Path.GetDirectoryName(processPath));
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
            var candidate = TryResolveDirectoryExecutable(fileName, directory);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    internal static bool HelpOutputEnablesLibrsvg(string helpOutput) =>
        helpOutput.Contains("--enable-librsvg", StringComparison.Ordinal);

    internal static RasterConversionStrategy GetRasterConversionStrategy(
        string outputPath,
        bool ffmpegSupportsSvgInput
    )
    {
        var outputExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        if (outputExtension == "png")
        {
            return RasterConversionStrategy.ResvgPngOnly;
        }

        return ffmpegSupportsSvgInput
            ? RasterConversionStrategy.DirectSvgWithFfmpeg
            : RasterConversionStrategy.ResvgThenFfmpeg;
    }

    internal static string GetVideoFrameExtension(bool ffmpegSupportsSvgInput) =>
        ffmpegSupportsSvgInput ? "svg" : "png";

    internal static async Task ConvertSvgToRasterAsync(
        string svgPath,
        string outputPath,
        string? resourcesDirectory,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var toolchain = await DetectToolchainAsync(logger, cancellationToken).ConfigureAwait(false);
        var strategy = GetRasterConversionStrategy(outputPath, toolchain.FfmpegSupportsSvgInput);

        switch (strategy)
        {
            case RasterConversionStrategy.DirectSvgWithFfmpeg:
                if (!toolchain.HasFfmpeg)
                {
                    throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
                }

                logger.ZLogDebug($"Raster output via ffmpeg SVG input. Out={outputPath}");
                await RunFfmpegImageAsync(
                        toolchain.FfmpegPath!,
                        svgPath,
                        outputPath,
                        resourcesDirectory,
                        logger,
                        cancellationToken,
                        "Ensure ffmpeg supports SVG input or use the ResvgSharp fallback."
                    )
                    .ConfigureAwait(false);
                return;

            case RasterConversionStrategy.ResvgPngOnly:
                logger.ZLogDebug($"Raster output via ResvgSharp PNG render. Out={outputPath}");
                await RasterImageRenderer.WritePngFileAsync(
                        svgPath,
                        outputPath,
                        resourcesDirectory,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return;

            case RasterConversionStrategy.ResvgThenFfmpeg:
                if (!toolchain.HasFfmpeg)
                {
                    throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
                }

                logger.ZLogDebug($"Raster output via ResvgSharp + ffmpeg. Out={outputPath}");
                var tempPng = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}.png");
                try
                {
                    await RasterImageRenderer.WritePngFileAsync(
                            svgPath,
                            tempPng,
                            resourcesDirectory,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    await RunFfmpegImageAsync(
                            toolchain.FfmpegPath!,
                            tempPng,
                            outputPath,
                            workingDirectory: null,
                            logger,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteFile(tempPng, logger);
                }

                return;

            default:
                throw new InvalidOperationException($"Unexpected raster conversion strategy: {strategy}");
        }
    }

    internal static async Task ConvertSvgFramesToVideoAsync(
        string svgFramesDir,
        double fps,
        string outputPath,
        string? resourcesDirectory,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var toolchain = await DetectToolchainAsync(logger, cancellationToken).ConfigureAwait(false);
        if (!toolchain.HasFfmpeg)
        {
            throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
        }

        if (toolchain.FfmpegSupportsSvgInput)
        {
            logger.ZLogDebug($"Video output via ffmpeg SVG input. Out={outputPath}");
            await RunFfmpegVideoAsync(
                    toolchain.FfmpegPath!,
                    svgFramesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(ffmpegSupportsSvgInput: true),
                    resourcesDirectory,
                    logger,
                    cancellationToken,
                    "Ensure ffmpeg supports SVG input or use the ResvgSharp fallback."
                )
                .ConfigureAwait(false);
            return;
        }

        logger.ZLogDebug($"Video output via ResvgSharp + ffmpeg. Out={outputPath}");
        var pngFramesDir = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}");
        try
        {
            await ConvertSvgFramesToPngAsync(
                    svgFramesDir,
                    pngFramesDir,
                    resourcesDirectory,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await RunFfmpegVideoAsync(
                    toolchain.FfmpegPath!,
                    pngFramesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(ffmpegSupportsSvgInput: false),
                    workingDirectory: null,
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

    private static string? TryResolveDirectoryExecutable(string fileName, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private static async Task<ConversionToolchain> DetectToolchainAsync(
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var ffmpegPath = TryResolveExecutable("ffmpeg");
        var ffmpegSupportsSvgInput =
            ffmpegPath is not null
            && await ProbeFfmpegSvgInputSupportAsync(ffmpegPath, logger, cancellationToken)
                .ConfigureAwait(false);

        logger.ZLogDebug(
            $"Toolchain detected. Ffmpeg={ffmpegPath ?? "(missing)"} FfmpegSvg={ffmpegSupportsSvgInput}"
        );

        return new ConversionToolchain(ffmpegPath, ffmpegSupportsSvgInput);
    }

    private static async Task<bool> ProbeFfmpegSvgInputSupportAsync(
        string ffmpegPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var helpOutput = await RunProcessForOutputAsync(
                    toolDisplayName: "ffmpeg",
                    executablePath: ffmpegPath,
                    args: ["-h"],
                    logger,
                    cancellationToken,
                    startFailureMessage:
                        "Please ensure ffmpeg is installed (bundled with the application or available in PATH).",
                    exitFailureMessage: "Failed to inspect ffmpeg build configuration."
                )
                .ConfigureAwait(false);

            var supportsLibrsvg = HelpOutputEnablesLibrsvg(helpOutput);
            logger.ZLogDebug(
                $"ffmpeg SVG input support detected. Path={ffmpegPath} SupportsLibrsvg={supportsLibrsvg}"
            );
            return supportsLibrsvg;
        }
        catch (InvalidOperationException ex)
        {
            logger.ZLogDebug(
                $"Failed to probe ffmpeg SVG input support. Path={ffmpegPath} Error={ex.Message}"
            );
            return false;
        }
    }

    private static string BuildUnavailableToolsMessage(string outputPath)
    {
        var extension = Path.GetExtension(outputPath)?.ToLowerInvariant();
        var extLabel = extension?.TrimStart('.').ToUpperInvariant() ?? string.Empty;

        return extension switch
        {
            ".png" =>
                $"Cannot generate {outputPath} because the built-in ResvgSharp renderer could not be used.",
            ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" =>
                $"Cannot generate {outputPath} because {extLabel} output requires ffmpeg. {FfmpegInstallHint}",
            ".mp4" or ".mov" or ".webm" or ".mkv" or ".avi" =>
                $"Cannot generate {outputPath} because video output requires ffmpeg. {FfmpegInstallHint}",
            _ =>
                $"Cannot generate {outputPath} because the required conversion toolchain is unavailable for the requested output format.",
        };
    }

    private static Task RunFfmpegImageAsync(
        string ffmpeg,
        string inputPath,
        string outputPath,
        string? workingDirectory,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage = "Ensure ffmpeg supports the requested output format."
    ) =>
        RunFfmpegAsync(
            ffmpeg,
            ["-y", "-i", inputPath, "-frames:v", "1", "-update", "1", outputPath],
            workingDirectory,
            logger,
            cancellationToken,
            exitFailureMessage
        );

    private static Task RunFfmpegVideoAsync(
        string ffmpeg,
        string framesDir,
        double fps,
        string outputPath,
        string frameExtension,
        string? workingDirectory,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage = "Ensure ffmpeg supports the requested output format."
    )
    {
        var framePattern = Path.Combine(framesDir, $"frame-%04d.{frameExtension}");
        var fpsValue = fps.ToString(CultureInfo.InvariantCulture);
        return RunFfmpegAsync(
            ffmpeg,
            ["-y", "-framerate", fpsValue, "-i", framePattern, outputPath],
            workingDirectory,
            logger,
            cancellationToken,
            exitFailureMessage
        );
    }

    private static Task RunFfmpegAsync(
        string ffmpeg,
        string[] args,
        string? workingDirectory,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage
    ) =>
        RunProcessAsync(
            toolDisplayName: "ffmpeg",
            executablePath: ffmpeg,
            args,
            workingDirectory,
            logger,
            cancellationToken,
            startFailureMessage:
                "Please ensure ffmpeg is installed (bundled with the application or available in PATH).",
            exitFailureMessage
        );

    private static async Task ConvertSvgFramesToPngAsync(
        string svgFramesDir,
        string pngFramesDir,
        string? resourcesDirectory,
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
            await RasterImageRenderer.WritePngFileAsync(
                    svgFramePath,
                    pngFramePath,
                    resourcesDirectory,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> RunProcessForOutputAsync(
        string toolDisplayName,
        string executablePath,
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken,
        string startFailureMessage,
        string exitFailureMessage
    )
    {
        logger.ZLogDebug($"Running {toolDisplayName} for output: {executablePath} {string.Join(' ', args)}");

        using var process = new Process();
        process.StartInfo.FileName = executablePath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

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
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{toolDisplayName} exited with code {process.ExitCode}. {exitFailureMessage}"
            );
        }

        return $"{stdout}\n{stderr}";
    }

    private static async Task RunProcessAsync(
        string toolDisplayName,
        string executablePath,
        string[] args,
        string? workingDirectory,
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
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }
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
