using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg;

internal static partial class Program
{
    private const int StatusProbeTimeoutMilliseconds = 5000;

    private static async Task<int> RunStatusAsync(bool json, CancellationToken cancellationToken)
    {
        var report = await CreateStatusReportAsync(cancellationToken).ConfigureAwait(false);
        if (json)
            Console.WriteLine(
                JsonSerializer.Serialize(report, StatusReportJsonContext.Default.StatusReport)
            );
        else
            WriteStatusReport(report);
        return 0;
    }

    private static async Task<StatusReport> CreateStatusReportAsync(
        CancellationToken cancellationToken
    )
    {
        var ffmpegPath = FindFfmpegExecutable();
        SvgConverter.SetFfmpegPath(ffmpegPath);

        var ffmpeg = await ProbeToolAsync("ffmpeg", ffmpegPath, "-version", cancellationToken)
            .ConfigureAwait(false);
        var mp4Codec = ffmpeg.Available ? SvgConverter.GetMp4CodecForStatus(ffmpegPath) : null;
        var rsvgConvert = await ProbeToolAsync(
                "rsvg-convert",
                SvgConverter.RsvgConvertPath,
                "--version",
                cancellationToken
            )
            .ConfigureAwait(false);
        var git = await ProbeToolAsync(
                "git",
                FindExecutableInPath("git") ?? string.Empty,
                "--version",
                cancellationToken
            )
            .ConfigureAwait(false);
        var tmux = OperatingSystem.IsWindows()
            ? StatusTool.Unavailable("tmux is supported on Unix-like platforms only.")
            : await ProbeToolAsync(
                    "tmux",
                    FindExecutableInPath("tmux") ?? string.Empty,
                    "-V",
                    cancellationToken
                )
                .ConfigureAwait(false);

        return new StatusReport(
            new StatusApplication(
                ThisAssembly.AssemblyInformationalVersion,
                Environment.ProcessPath,
                !RuntimeFeature.IsDynamicCodeSupported
            ),
            new StatusPlatform(
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription
            ),
            new StatusRenderers(
                new StatusTool(
                    SvgConverter.IsResvgAvailable,
                    SvgConverter.BundledResvgVersion,
                    null,
                    "bundled",
                    SvgConverter.IsResvgAvailable
                        ? null
                        : "Native library failed to load or initialize."
                ),
                rsvgConvert,
                ffmpeg with
                {
                    Source = ffmpeg.Available ? GetFfmpegSource(ffmpeg.Path) : null,
                }
            ),
            new StatusOptionalFeatures(git, tmux),
            GetThemeStatus(),
            new StatusTerminal(
                GetAnsiColorStatus(),
                new Dictionary<string, string?>
                {
                    ["TERM"] = Environment.GetEnvironmentVariable("TERM"),
                    ["COLORTERM"] = Environment.GetEnvironmentVariable("COLORTERM"),
                    ["FORCE_COLOR"] = Environment.GetEnvironmentVariable("FORCE_COLOR"),
                    ["NO_COLOR"] = Environment.GetEnvironmentVariable("NO_COLOR"),
                    ["CI"] = Environment.GetEnvironmentVariable("CI"),
                    ["TF_BUILD"] = Environment.GetEnvironmentVariable("TF_BUILD"),
                }
            ),
            GetOutputFormats(
                SvgConverter.IsResvgAvailable,
                SvgConverter.IsRsvgConvertAvailable,
                ffmpeg.Available && SvgConverter.FfmpegSupportsSvg,
                mp4Codec,
                ffmpeg.Available
            )
        );
    }

    private static async Task<StatusTool> ProbeToolAsync(
        string name,
        string path,
        string versionArgument,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return StatusTool.Unavailable($"{name} was not found.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add(versionArgument);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            if (
                await Task.WhenAny(
                        exitTask,
                        Task.Delay(StatusProbeTimeoutMilliseconds, cancellationToken)
                    )
                    .ConfigureAwait(false) != exitTask
            )
            {
                process.Kill(entireProcessTree: true);
                await exitTask.ConfigureAwait(false);
                return StatusTool.Unavailable($"{name} did not respond within 5 seconds.", path);
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return StatusTool.Unavailable($"{name} exited with code {process.ExitCode}.", path);

            var version = string.Concat(output, error)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.Trim();
            return new StatusTool(true, version, path, null, null);
        }
        catch (Exception ex)
            when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return StatusTool.Unavailable(ex.Message, path);
        }
    }

    private static StatusThemes GetThemeStatus()
    {
        try
        {
            var entries = new ThemeCatalog().Entries.ToArray();
            return new StatusThemes(
                entries.Count(entry => entry.IsBuiltIn),
                entries.Count(entry => !entry.IsBuiltIn),
                ThemeCatalog.UserThemeDirectory,
                null
            );
        }
        catch (Exception ex)
            when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            return new StatusThemes(0, 0, ThemeCatalog.UserThemeDirectory, ex.Message);
        }
    }

    private static StatusAvailability GetAnsiColorStatus()
    {
        if (Console.IsOutputRedirected)
            return new StatusAvailability(false, "Standard output is redirected.");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            return new StatusAvailability(false, "NO_COLOR is set.");
        if (
            string.Equals(
                Environment.GetEnvironmentVariable("TERM"),
                "dumb",
                StringComparison.Ordinal
            )
        )
            return new StatusAvailability(false, "TERM is dumb.");
        return new StatusAvailability(SupportsAnsiColors(), null);
    }

    private static StatusOutputFormat[] GetOutputFormats(
        bool resvgAvailable,
        bool rsvgConvertAvailable,
        bool ffmpegSvgAvailable,
        string? mp4Codec,
        bool ffmpegAvailable
    )
    {
        var rasterAvailable = resvgAvailable || rsvgConvertAvailable || ffmpegSvgAvailable;
        string? pngConverter = null;
        if (resvgAvailable)
            pngConverter = "resvg";
        else if (ffmpegSvgAvailable)
            pngConverter = "ffmpeg";
        else if (rsvgConvertAvailable)
            pngConverter = "rsvg-convert";
        return
        [
            new("svg", true, ".svg", "native", null),
            new("png", rasterAvailable, ".png", pngConverter, null),
            new("jpg", rasterAvailable && ffmpegAvailable, ".jpg, .jpeg", "ffmpeg", "mjpeg"),
            new("gif", rasterAvailable && ffmpegAvailable, ".gif", "ffmpeg", "gif"),
            new("mp4", rasterAvailable && mp4Codec is not null, ".mp4", "ffmpeg", mp4Codec),
            new("webm", rasterAvailable && ffmpegAvailable, ".webm", "ffmpeg", "vp9"),
        ];
    }

    private static string GetFfmpegSource(string? path) =>
        path is not null
        && AppPaths
            .GetBundledAssetDirectories()
            .Any(directory =>
                string.Equals(
                    path,
                    Path.Combine(
                        directory,
                        "ffmpeg",
                        OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"
                    ),
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    path,
                    Path.Combine(directory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ? "bundled"
            : "PATH";

    private static void WriteStatusReport(StatusReport report)
    {
        Console.WriteLine($"console2svg  {report.Application.Version}");
        Console.WriteLine(
            $"platform     {report.Platform.OperatingSystem} ({report.Platform.Architecture})"
        );
        Console.WriteLine($"executable   {report.Application.ExecutablePath ?? "(unknown)"}");
        Console.WriteLine();
        Console.WriteLine("Rendering");
        WriteStatusTool("resvg", report.Renderers.Resvg);
        WriteStatusTool("rsvg-convert", report.Renderers.RsvgConvert);
        WriteStatusTool("ffmpeg", report.Renderers.Ffmpeg);
        Console.WriteLine();
        Console.WriteLine("Optional features");
        WriteStatusTool("git", report.OptionalFeatures.Git);
        WriteStatusTool("tmux", report.OptionalFeatures.Tmux);
        Console.WriteLine();
        Console.WriteLine("Themes");
        var themeStatus = report.Themes.Error is null ? "available" : "unavailable";
        var themeDetails = report.Themes.Error is null
            ? $"{report.Themes.BuiltIn} built-in, {report.Themes.Installed} installed"
            : report.Themes.Error;
        WriteStatusLine("themes", themeStatus, themeDetails, report.Themes.Error is null);
        Console.WriteLine();
        Console.WriteLine("Terminal");
        WriteStatusAvailability("ANSI color", report.Terminal.AnsiColor);
        Console.WriteLine();
        Console.WriteLine("Support output formats");
        foreach (var format in report.OutputFormats)
            WriteOutputFormatStatus(format);
    }

    private static void WriteStatusTool(string name, StatusTool tool)
    {
        var status = tool.Available ? "available" : "unavailable";
        var path = tool.Available ? tool.Path ?? "(bundled)" : "";
        var details = tool.Available ? tool.Version : tool.Error;
        var value = $"{status.PadRight(12)}{path.PadRight(24)}{details ?? string.Empty}".TrimEnd();
        Console.WriteLine($"  {name.PadRight(14)}{ColorizeStatus(value, tool.Available)}");
    }

    private static void WriteStatusAvailability(string name, StatusAvailability availability)
    {
        WriteStatusLine(
            name,
            availability.Available ? "available" : "unavailable",
            availability.Reason,
            availability.Available
        );
    }

    private static void WriteStatusLine(string name, string status, string? details, bool available)
    {
        var value = $"{status.PadRight(12)}{(details is null ? "" : details)}";
        Console.WriteLine($"  {name.PadRight(14)}{ColorizeStatus(value, available)}");
    }

    private static void WriteOutputFormatStatus(StatusOutputFormat format)
    {
        var status = $"{(format.Available ? "available" : "unavailable")}({format.Extensions})";
        var details =
            $"{format.Converter ?? "none"}{(format.Codec is null ? "" : $" ({format.Codec})")}";
        Console.WriteLine(
            $"  {format.Name.PadRight(14)}{ColorizeStatus(status.PadRight(27) + details, format.Available)}"
        );
    }

    private static string ColorizeStatus(string value, bool available)
    {
        if (!SupportsAnsiColors())
            return value;
        var color = available ? "32" : "90";
        return $"\x1b[{color}m{value}\x1b[0m";
    }
}

internal sealed record StatusReport(
    StatusApplication Application,
    StatusPlatform Platform,
    StatusRenderers Renderers,
    StatusOptionalFeatures OptionalFeatures,
    StatusThemes Themes,
    StatusTerminal Terminal,
    StatusOutputFormat[] OutputFormats
);

internal sealed record StatusApplication(string Version, string? ExecutablePath, bool NativeAot);

internal sealed record StatusPlatform(
    string OperatingSystem,
    string Architecture,
    string Framework
);

internal sealed record StatusRenderers(StatusTool Resvg, StatusTool RsvgConvert, StatusTool Ffmpeg);

internal sealed record StatusOptionalFeatures(StatusTool Git, StatusTool Tmux);

internal sealed record StatusThemes(int BuiltIn, int Installed, string Directory, string? Error);

internal sealed record StatusTerminal(
    StatusAvailability AnsiColor,
    Dictionary<string, string?> Environment
);

internal sealed record StatusAvailability(bool Available, string? Reason);

internal sealed record StatusOutputFormat(
    string Name,
    bool Available,
    string Extensions,
    string? Converter,
    string? Codec
);

internal sealed record StatusTool(
    bool Available,
    string? Version,
    string? Path,
    string? Source,
    string? Error
)
{
    public static StatusTool Unavailable(string error, string? path = null) =>
        new(false, null, path, null, error);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusReport))]
internal sealed partial class StatusReportJsonContext : JsonSerializerContext { }
