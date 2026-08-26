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
    private static ILoggerFactory CreateLoggerFactory(
        bool verbose,
        string? logPath,
        EmbeddedLogCollector? embeddedLogCollector = null
    )
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            if (verbose)
            {
                var path = string.IsNullOrWhiteSpace(logPath) ? "console2svg.log" : logPath;
                builder.AddZLoggerFile(
                    path,
                    options =>
                    {
                        options.FileShared = false;
                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter(
                                $"[{0:local}] ",
                                (in template, in info) => template.Format(info.Timestamp)
                            );
                        });
                    }
                );
            }
            if (embeddedLogCollector is not null)
            {
                builder.AddProvider(embeddedLogCollector);
            }

            if (verbose || embeddedLogCollector is not null)
            {
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.None);
            }
        });
    }

    private static int? TryGetConsoleWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            return w > 0 ? w : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetConsoleHeight()
    {
        try
        {
            var h = Console.WindowHeight;
            return h > 0 ? h : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveSize(
        int? explicitValue,
        bool adjust,
        Func<int?> detectTerminalSize,
        int defaultValue
    )
    {
        if (explicitValue.HasValue)
        {
            return explicitValue.Value;
        }

        return adjust ? detectTerminalSize() ?? defaultValue : defaultValue;
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    // Video file extensions that are handled by the frame-sequence → ffmpeg path.
    // GIF is included here because the primary use-case for terminal recordings is
    // an animated GIF; users who want a static GIF can specify --mode image separately.
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4",
        "webm",
        "avi",
        "mov",
        "mkv",
        "ogv",
        "flv",
        "ts",
        "wmv",
        "m4v",
        "gif",
    };

    private static bool IsVideoFormat(string extension) => VideoExtensions.Contains(extension);

    private static bool IsInteractiveRecordingFormat(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).TrimStart('.');
        return string.IsNullOrEmpty(extension)
            || string.Equals(extension, "svg", StringComparison.OrdinalIgnoreCase)
            || IsVideoFormat(extension);
    }

    /// <summary>
    /// Determines whether ffmpeg is required to produce the final output format.
    /// Video formats always need ffmpeg; PNG can be produced via rsvg-convert or
    /// bundled resvg alone; all other raster formats (gif, jpg, webp, etc.) also need ffmpeg.
    /// If <see cref="AppOptions.IsModeExplicit"/> is set, the explicit mode takes
    /// precedence over the extension (e.g. <c>--mode image</c> for a static .gif).
    /// </summary>
    private static bool RequiresFfmpeg(AppOptions options, string extension)
    {
        var useVideoPath = options.IsModeExplicit
            ? options.Mode is OutputMode.Video or OutputMode.Repeat
            : IsVideoFormat(extension);
        var isPngOutput = string.Equals(extension, "png", StringComparison.Ordinal);
        return useVideoPath || !isPngOutput;
    }

    /// <summary>
    /// Finds the ffmpeg executable to use for format conversion.
    /// Preference order: application ffmpeg/ subdirectory, binary next to the host (legacy bundled), then PATH.
    /// </summary>
    private static string FindFfmpegExecutable()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        var appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appDir))
        {
            // 1. ffmpeg bundled in a subdirectory. This keeps ffmpeg off the user's PATH
            //    while still making it available to console2svg.
            var bundledSubDir = Path.Combine(appDir, "ffmpeg", exeName);
            if (File.Exists(bundledSubDir))
            {
                return bundledSubDir;
            }
        }

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrEmpty(exeDir))
        {
            // 2. Legacy bundled layout: ffmpeg next to the process executable.
            var bundled = Path.Combine(exeDir, exeName);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        // 3. Rely on PATH
        return exeName;
    }
}
