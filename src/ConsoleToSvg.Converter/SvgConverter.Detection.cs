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

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

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

        var tempDir = Path.Combine(Path.GetTempPath(), $"c2s-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempSvg = Path.Combine(tempDir, "probe.svg");
        var tempPng = Path.Combine(tempDir, "probe.png");

        try
        {
            File.WriteAllText(
                tempSvg,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>"
            );

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
            try
            {
                if (File.Exists(tempSvg))
                    File.Delete(tempSvg);
            }
            catch
            {
                // Best-effort cleanup; ignore any errors.
            }

            try
            {
                if (File.Exists(tempPng))
                    File.Delete(tempPng);
            }
            catch
            {
                // Best-effort cleanup; ignore any errors.
            }

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir);
            }
            catch
            {
                // Best-effort cleanup; ignore any errors.
            }
        }
    }

    /// <summary>
    /// Checks whether ffmpeg has a specific video encoder available by parsing
    /// the output of <c>ffmpeg -encoders</c>.
    /// </summary>
    private static bool CheckFfmpegEncoder(string encoderName)
    {
        var exe = FindFfmpegForDetection();
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = exe;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-encoders");

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Encoder lines look like: " V....D libx264 ..."
            // The encoder name is the third whitespace-separated token.
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("V", StringComparison.Ordinal))
                {
                    var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1] == encoderName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
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
        Math.Round(px, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static int ToPxInt(double px) => (int)Math.Round(px, MidpointRounding.AwayFromZero);
}
