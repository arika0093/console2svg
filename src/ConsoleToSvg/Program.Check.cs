using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static string GetOutputFormat(AppOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Format))
        {
            return options.Format;
        }

        var extension = Path.GetExtension(options.OutputPath).TrimStart('.').ToLowerInvariant();
        return OutputFormatCatalog.TryResolve(extension, out var format)
            ? format!.Extension
            : extension;
    }

    private static string GetEncodedOutputPath(string outputPath, string format)
    {
        var requestedExtension = Path.GetExtension(outputPath).TrimStart('.');
        if (string.Equals(requestedExtension, format, StringComparison.OrdinalIgnoreCase))
        {
            return outputPath;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        return Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.{format}"
        );
    }

    private static void CompleteEncodedOutput(string encodedPath, string outputPath)
    {
        if (!string.Equals(encodedPath, outputPath, StringComparison.Ordinal))
        {
            File.Move(encodedPath, outputPath, overwrite: true);
        }
    }

    private static void CleanupEncodedOutput(string encodedPath, string outputPath)
    {
        if (
            !string.Equals(encodedPath, outputPath, StringComparison.Ordinal)
            && File.Exists(encodedPath)
        )
        {
            try
            {
                File.Delete(encodedPath);
            }
            catch (IOException)
            {
                // Ignore cleanup failures
            }
        }
    }

    private static void WriteCheckReport()
    {
        SvgConverter.SetFfmpegPath(FindFfmpegExecutable());
        var width = TryGetConsoleWidth() ?? DefaultWidth;
        var height = TryGetConsoleHeight() ?? DefaultHeight;
        var shell =
            Environment.GetEnvironmentVariable(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "COMSPEC" : "SHELL"
            ) ?? "unknown";

        var ffmpeg = GetFfmpegDetails();
        var rsvg = GetToolDetails(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "rsvg-convert.exe"
                : "rsvg-convert",
            "--version"
        );
        var color = SupportsAnsiColors();
        var formatLabels = OutputFormatCatalog.All.Select(format =>
            $"{format.Name}({string.Join(',', format.Extensions)})"
        ).ToArray();
        var formatLabelWidth = formatLabels.Max(label => label.Length);
        var formatLines = string.Join(
            Environment.NewLine,
            OutputFormatCatalog.All.Select((format, index) =>
                $"* {formatLabels[index].PadRight(formatLabelWidth)} "
                + $"[{Capability("Image", format.SupportsImage, color)}|"
                + $"{Capability("Animation", format.SupportsAnimation, color)}]"
                + GetCodecSummary(format.Extension, ffmpeg.Encoders)
            )
        );

        Console.WriteLine(
            $$"""
            {{Heading("console2svg", color)}}
            Version:   {{ThisAssembly.AssemblyInformationalVersion}} [{{ThisAssembly.GitCommitDate:yyyy/MM/dd HH:mm:ss}} UTC]
            Build:     {{RuntimeInformation.FrameworkDescription}}

            {{Heading("user environments:", color)}}
            OS:        {{RuntimeInformation.OSDescription}} {{RuntimeInformation.ProcessArchitecture}}
            Terminal:  {{Path.GetFileName(shell)}} [{{width}}x{{height}}]
            Color:     {{GetColorSupportDescription()}}

            {{Heading("3rd party tools:", color)}}
            {{Availability(true, color)}} resvg (built-in, ver {{SvgConverter.BundledResvgVersion}})
            {{Availability(ffmpeg.Available, color)}} ffmpeg{{ffmpeg.Description}}
            {{Availability(rsvg.Available, color)}} rsvg-convert{{rsvg.Description}}

            {{Heading("supported formats:", color)}}
            {{formatLines}}
            """
        );
    }

    private static FfmpegDetails GetFfmpegDetails()
    {
        var executable = FindFfmpegExecutable();
        var displayPath = File.Exists(executable)
            ? Path.GetFullPath(executable)
            : FindExecutableInPath(executable) ?? executable;
        try
        {
            using var versionProcess = StartProcess(executable, "-hide_banner", "-version");
            var versionLine = versionProcess.StandardOutput.ReadLine();
            versionProcess.WaitForExit();
            if (versionProcess.ExitCode != 0)
            {
                return new(false, string.Empty, new HashSet<string>());
            }

            using var encoderProcess = StartProcess(executable, "-hide_banner", "-encoders");
            var encoderOutput = encoderProcess.StandardOutput.ReadToEnd();
            encoderProcess.WaitForExit();
            var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in encoderOutput.Split('\n'))
            {
                var parts = line.Trim().Split(
                    [' ', '\t'],
                    StringSplitOptions.RemoveEmptyEntries
                );
                if (parts.Length >= 2 && parts[0].StartsWith('V'))
                {
                    encoders.Add(parts[1]);
                }
            }

            var version = versionLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(2)
                .FirstOrDefault();
            var description =
                $" ({displayPath}{(string.IsNullOrWhiteSpace(version) ? "" : $", ver {version}")})";
            return new(true, description, encoders);
        }
        catch
        {
            return new(false, string.Empty, new HashSet<string>());
        }
    }

    private static Process StartProcess(string executable, params string[] arguments)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        return process;
    }

    private static string GetCodecSummary(string format, IReadOnlySet<string> encoders)
    {
        string[] candidates = format switch
        {
            "mp4" => ["libx264", "mpeg4"],
            "webm" => ["libvpx", "libvpx-vp9", "libaom-av1", "libsvtav1"],
            "avi" => ["mpeg4", "libx264", "mjpeg"],
            "mov" => ["libx264", "prores", "prores_ks"],
            "mkv" => ["libx264", "libvpx-vp9", "libaom-av1"],
            "ogv" => ["libtheora"],
            "flv" => ["flv", "libx264"],
            "ts" => ["libx264", "mpeg2video"],
            "wmv" => ["wmv2"],
            "m4v" => ["mpeg4", "libx264"],
            "gif" => ["gif"],
            _ => [],
        };
        var available = candidates.Where(encoders.Contains).ToArray();
        if (available.Length == 0)
        {
            return string.Empty;
        }

        return $" {string.Join(", ", available)}";
    }

    private static ToolDetails GetToolDetails(string executableName, params string[] arguments)
    {
        var executable = FindExecutableInPath(executableName);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new(false, string.Empty);
        }

        try
        {
            using var process = StartProcess(executable, arguments);
            var versionLine = process.StandardOutput.ReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return new(false, string.Empty);
            }

            var version = versionLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            var description =
                $" ({executable}{(string.IsNullOrWhiteSpace(version) ? "" : $", ver {version}")})";
            return new(true, description);
        }
        catch
        {
            return new(false, string.Empty);
        }
    }

    private static string GetColorSupportDescription()
    {
        if (!SupportsAnsiColors())
        {
            return "unavailable";
        }

        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM");
        if (
            string.Equals(colorTerm, "truecolor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(colorTerm, "24bit", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "available (true color)";
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        return term?.Contains("256color", StringComparison.OrdinalIgnoreCase) == true
            ? "available (256 colors)"
            : "available (ANSI)";
    }

    private static string Heading(string value, bool color) =>
        color ? $"\x1b[1;36m{value}\x1b[0m" : value;

    private static string Capability(string name, bool available, bool color)
    {
        var value = available ? name : new string('-', name.Length);
        if (!color)
        {
            return value;
        }

        return available ? $"\x1b[32m{value}\x1b[0m" : $"\x1b[2m{value}\x1b[0m";
    }

    private static string Availability(bool available, bool color)
    {
        var marker = available ? "✓" : "--";
        if (!color)
        {
            return marker;
        }

        return available ? $"\x1b[32m{marker}\x1b[0m" : $"\x1b[31m{marker}\x1b[0m";
    }

    private sealed record FfmpegDetails(
        bool Available,
        string Description,
        IReadOnlySet<string> Encoders
    );

    private sealed record ToolDetails(bool Available, string Description);
}
