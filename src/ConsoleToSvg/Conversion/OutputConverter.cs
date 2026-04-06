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

internal sealed class ConversionToolchain
{
    internal ConversionToolchain(string? ffmpegPath, bool ffmpegSupportsSvgInput, string? resvgPath)
    {
        FfmpegPath = ffmpegPath;
        FfmpegSupportsSvgInput = ffmpegSupportsSvgInput;
        ResvgPath = resvgPath;
    }

    internal string? FfmpegPath { get; }

    internal bool FfmpegSupportsSvgInput { get; }

    internal string? ResvgPath { get; }

    internal bool HasFfmpeg => !string.IsNullOrWhiteSpace(FfmpegPath);

    internal bool HasResvg => !string.IsNullOrWhiteSpace(ResvgPath);
}

internal static class OutputConverter
{
    // Animated formats are routed through the frame-sequence pipeline unless image mode is explicit.
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "webm", "avi", "mov", "mkv", "ogv", "flv", "ts", "wmv", "m4v", "gif",
    };

    internal static bool IsVideoFormat(string extension) => VideoExtensions.Contains(extension);

    internal static string GetExecutableFileName(string toolName, bool isWindows) =>
        isWindows ? $"{toolName}.exe" : toolName;

    // Prefer bundled tools first, then the current working directory, then PATH.
    internal static string? TryResolveExecutable(string toolName)
        => TryResolveExecutable(
            toolName,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Environment.ProcessPath,
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariable("PATH")
        );

    internal static string? TryResolveExecutable(
        string toolName,
        bool isWindows,
        string? processPath,
        string? currentDirectory,
        string? pathEnvironment
    )
    {
        var fileName = GetExecutableFileName(toolName, isWindows);

        var bundled = TryResolveDirectoryExecutable(fileName, Path.GetDirectoryName(processPath));
        if (bundled is not null)
        {
            return bundled;
        }

        var fromCurrentDirectory = TryResolveDirectoryExecutable(fileName, currentDirectory);
        if (fromCurrentDirectory is not null)
        {
            return fromCurrentDirectory;
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

    // Choose the cheapest viable path: direct ffmpeg SVG input, then resvg, otherwise fail.
    internal static RasterConversionStrategy? GetRasterConversionStrategy(
        string outputPath,
        bool ffmpegSupportsSvgInput,
        bool resvgAvailable
    )
    {
        if (ffmpegSupportsSvgInput)
        {
            return RasterConversionStrategy.DirectSvgWithFfmpeg;
        }

        if (!resvgAvailable)
        {
            return null;
        }

        var outputExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        return outputExtension == "png"
            ? RasterConversionStrategy.ResvgPngOnly
            : RasterConversionStrategy.ResvgThenFfmpeg;
    }

    // Video uses PNG frames only when resvg has to rasterize the SVG frames first.
    internal static string GetVideoFrameExtension(bool useResvg) =>
        useResvg ? "png" : "svg";

    internal static async Task ConvertSvgToRasterAsync(
        string svgPath,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var toolchain = await DetectToolchainAsync(logger, cancellationToken).ConfigureAwait(false);
        var strategy = GetRasterConversionStrategy(
            outputPath,
            toolchain.FfmpegSupportsSvgInput,
            toolchain.HasResvg
        );

        if (strategy is null)
        {
            throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
        }

        switch (strategy.Value)
        {
            case RasterConversionStrategy.DirectSvgWithFfmpeg:
                logger.ZLogDebug($"Raster output via ffmpeg SVG input. Out={outputPath}");
                await RunFfmpegImageAsync(
                        toolchain.FfmpegPath!,
                        svgPath,
                        outputPath,
                        logger,
                        cancellationToken,
                        "Ensure ffmpeg supports SVG input or install resvg."
                    )
                    .ConfigureAwait(false);
                return;

            case RasterConversionStrategy.ResvgPngOnly:
                logger.ZLogDebug($"Raster output via resvg only. Out={outputPath}");
                await RunResvgAsync(toolchain.ResvgPath!, svgPath, outputPath, logger, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case RasterConversionStrategy.ResvgThenFfmpeg:
                if (!toolchain.HasFfmpeg)
                {
                    throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
                }

                // resvg handles SVG -> PNG, then ffmpeg converts PNG to the requested target format.
                logger.ZLogDebug($"Raster output via resvg + ffmpeg. Out={outputPath}");
                var tempPng = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}.png");
                try
                {
                    await RunResvgAsync(toolchain.ResvgPath!, svgPath, tempPng, logger, cancellationToken)
                        .ConfigureAwait(false);
                    await RunFfmpegImageAsync(
                            toolchain.FfmpegPath!,
                            tempPng,
                            outputPath,
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
                throw new InvalidOperationException($"Unexpected raster conversion strategy: {strategy.Value}");
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
        var toolchain = await DetectToolchainAsync(logger, cancellationToken).ConfigureAwait(false);

        if (toolchain.FfmpegSupportsSvgInput)
        {
            // ffmpeg can read SVG input directly, so there is no reason to involve resvg.
            logger.ZLogDebug($"Video output via ffmpeg SVG input. Out={outputPath}");
            await RunFfmpegVideoAsync(
                    toolchain.FfmpegPath!,
                    framesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(useResvg: false),
                    logger,
                    cancellationToken,
                    "Ensure ffmpeg supports SVG input or install resvg."
                )
                .ConfigureAwait(false);
            return;
        }

        if (!toolchain.HasResvg)
        {
            throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
        }

        if (!toolchain.HasFfmpeg)
        {
            throw new InvalidOperationException(BuildUnavailableToolsMessage(outputPath));
        }

        // When ffmpeg cannot read SVG input, render each frame to PNG first.
        logger.ZLogDebug($"Video output via resvg + ffmpeg. Out={outputPath}");
        var pngFramesDir = Path.Combine(Path.GetTempPath(), $"c2s-{Guid.NewGuid():N}");
        try
        {
            await ConvertSvgFramesToPngAsync(
                    toolchain.ResvgPath!,
                    framesDir,
                    pngFramesDir,
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await RunFfmpegVideoAsync(
                    toolchain.FfmpegPath!,
                    pngFramesDir,
                    fps,
                    outputPath,
                    GetVideoFrameExtension(useResvg: true),
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

    // Resolve a tool from a specific directory without consulting the shell.
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
        // Probe once so all later decisions use the same tool availability snapshot.
        var ffmpegPath = TryResolveExecutable("ffmpeg");
        var resvgPath = TryResolveExecutable("resvg");
        var ffmpegSupportsSvgInput =
            ffmpegPath is not null
            && await ProbeFfmpegSvgInputSupportAsync(ffmpegPath, logger, cancellationToken)
                .ConfigureAwait(false);

        logger.ZLogDebug(
            $"Toolchain detected. Ffmpeg={ffmpegPath ?? "(missing)"} FfmpegSvg={ffmpegSupportsSvgInput} Resvg={resvgPath ?? "(missing)"}"
        );

        return new ConversionToolchain(ffmpegPath, ffmpegSupportsSvgInput, resvgPath);
    }

    private static async Task<bool> ProbeFfmpegSvgInputSupportAsync(
        string ffmpegPath,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // `ffmpeg -h` includes the build configuration line that exposes `--enable-librsvg`.
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

    // Keep the user-facing failure terse; the search order is deterministic from the code path above.
    private static string BuildUnavailableToolsMessage(string outputPath) =>
        $"Cannot generate {outputPath} because ffmpeg and resvg cannot be used for this conversion.";

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
        string ffmpeg,
        string inputPath,
        string outputPath,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage = "Ensure ffmpeg supports the requested output format."
    ) =>
        RunFfmpegAsync(
            ffmpeg,
            ["-y", "-i", inputPath, "-frames:v", "1", "-update", "1", outputPath],
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
            logger,
            cancellationToken,
            exitFailureMessage
        );
    }

    private static Task RunFfmpegAsync(
        string ffmpeg,
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken,
        string exitFailureMessage
    ) =>
        RunProcessAsync(
            toolDisplayName: "ffmpeg",
            executablePath: ffmpeg,
            args,
            logger,
            cancellationToken,
            startFailureMessage:
                "Please ensure ffmpeg is installed (bundled with the application or available in PATH).",
            exitFailureMessage
        );

    private static async Task ConvertSvgFramesToPngAsync(
        string resvg,
        string svgFramesDir,
        string pngFramesDir,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(pngFramesDir);
        // Keep frame names aligned so ffmpeg can still consume a `%04d` sequence.
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
