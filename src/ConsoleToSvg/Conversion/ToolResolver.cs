using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ConsoleToSvg.Conversion;

internal static class ToolResolver
{
    internal const string ResvgEnvironmentVariable = "CONSOLE2SVG_RESVG";
    internal const string FfmpegEnvironmentVariable = "CONSOLE2SVG_FFMPEG";

    public static string ResolveResvg() =>
        ResolveTool(
            "resvg",
            ResvgEnvironmentVariable,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "resvg.exe" : "resvg",
            """
            resvg is required for raster output (.png/.jpg/.webp/.gif/.mp4/.webm).
            Set CONSOLE2SVG_RESVG, place resvg next to console2svg, or install resvg and ensure it is on PATH.
            """
        );

    public static string ResolveFfmpeg() =>
        ResolveTool(
            "ffmpeg",
            FfmpegEnvironmentVariable,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg",
            """
            ffmpeg is required for .jpg/.webp/.gif/.mp4/.webm output.
            Set CONSOLE2SVG_FFMPEG, place ffmpeg next to console2svg, or install ffmpeg and ensure it is on PATH.
            """
        );

    internal static string? TryResolveResvg() =>
        TryResolveTool(
            "resvg",
            ResvgEnvironmentVariable,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "resvg.exe" : "resvg"
        );

    internal static string? TryResolveFfmpeg() =>
        TryResolveTool(
            "ffmpeg",
            FfmpegEnvironmentVariable,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg"
        );

    private static string ResolveTool(
        string commandName,
        string envVar,
        string sidecarName,
        string guidance
    )
    {
        var resolved = TryResolveTool(commandName, envVar, sidecarName);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        throw new InvalidOperationException(guidance.Trim());
    }

    private static string? TryResolveTool(
        string commandName,
        string envVar,
        string sidecarName
    )
    {
        var explicitPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        var sidecarPath = Path.Combine(AppContext.BaseDirectory, sidecarName);
        if (File.Exists(sidecarPath))
        {
            return sidecarPath;
        }

        return TryResolveFromPath(commandName);
    }

    private static string? TryResolveFromPath(string commandName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in EnumeratePathCandidates(directory, commandName))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePathCandidates(string directory, string commandName)
    {
        yield return Path.Combine(directory, commandName);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(extensions))
        {
            yield return Path.Combine(directory, $"{commandName}.exe");
            yield break;
        }

        foreach (var extension in extensions
                     .Split(';', StringSplitOptions.RemoveEmptyEntries)
                     .Select(static ext => ext.Trim()))
        {
            yield return Path.Combine(directory, commandName + extension);
        }
    }
}
