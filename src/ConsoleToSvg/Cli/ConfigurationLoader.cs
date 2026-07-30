using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VYaml;
using VYaml.Annotations;
using VYaml.Serialization;

namespace ConsoleToSvg.Cli;

/// <summary>Loads NativeAOT-safe, source-generated YAML appearance configuration.</summary>
internal static class ConfigurationLoader
{
    public static bool TryExtract(string[] args, out List<string> configPaths, out string[] remainingArgs, out string? error)
    {
        configPaths = [];
        var remaining = new List<string>();
        remainingArgs = [];
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--") { remaining.AddRange(args[i..]); break; }
            if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                var path = arg["--config=".Length..];
                if (string.IsNullOrWhiteSpace(path)) { error = "--config requires a YAML file path."; return false; }
                configPaths.Add(path);
                continue;
            }
            if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Length || args[i].StartsWith("-", StringComparison.Ordinal)) { error = "--config requires a YAML file path."; return false; }
                configPaths.Add(args[i]);
                continue;
            }
            remaining.Add(arg);
        }

        remainingArgs = [.. remaining];
        return true;
    }

    public static bool TryLoad(string path, out string[] args, out string? error)
    {
        var output = new List<string>();
        if (!TryLoadCore(Path.GetFullPath(path), new HashSet<string>(StringComparer.OrdinalIgnoreCase), output, out error))
        {
            args = [];
            return false;
        }
        args = [.. output];
        return true;
    }

    private static bool TryLoadCore(string path, HashSet<string> stack, List<string> args, out string? error)
    {
        error = null;
        if (!stack.Add(path)) { error = $"Configuration include cycle detected: {path}"; return false; }
        try
        {
            if (!File.Exists(path)) { error = $"Configuration file was not found: {path}"; return false; }
            var config = YamlSerializer.Deserialize<ConsoleToSvgConfiguration>(File.ReadAllBytes(path));
            var directory = Path.GetDirectoryName(path)!;
            if (config.Include is not null)
            {
                foreach (var include in config.Include)
                {
                    if (!TryLoadCore(Path.GetFullPath(Path.Combine(directory, include)), stack, args, out error)) return false;
                }
            }
            AddConfigurationArguments(config, args);
            return true;
        }
        catch (Exception ex) when (ex is IOException or YamlException)
        {
            error = $"Could not load configuration file {path}: {ex.Message}";
            return false;
        }
        finally { stack.Remove(path); }
    }

    private static void AddConfigurationArguments(ConsoleToSvgConfiguration config, List<string> args)
    {
        AddInputArguments(config.Input, args);
        AddOutputArguments(config.Output, args);
        AddCaptureArguments(config.Capture, args);
        AddVideoArguments(config.Video, args);
        AddReplayArguments(config.Replay, args);
        AddConversionArguments(config.Conversion, args);
        AddRuntimeArguments(config.Runtime, args);
        AddAppearanceArguments(config.Appearance, args);
    }

    private static void AddInputArguments(InputConfiguration? input, List<string> args)
    {
        if (input is not null) Add(args, "--in", input.CastPath);
    }

    private static void AddOutputArguments(OutputConfiguration? output, List<string> args)
    {
        if (output is null) return;
        Add(args, "--out", output.Path);
        Add(args, "--mode", output.Mode);
        AddFlag(args, "--stdout", output.StdOut);
        Add(args, "--save-frames", output.SaveFramesDir);
        Add(args, "--size", output.Size);
    }

    private static void AddCaptureArguments(CaptureConfiguration? capture, List<string> args)
    {
        if (capture is null) return;
        Add(args, "--width", capture.Width);
        Add(args, "--height", capture.Height);
        Add(args, "--frame", capture.Frame);
        Add(args, "--time", capture.Time);
        Add(args, "--crop-top", capture.CropTop);
        Add(args, "--crop-right", capture.CropRight);
        Add(args, "--crop-bottom", capture.CropBottom);
        Add(args, "--crop-left", capture.CropLeft);
        Add(args, "--save-cast", capture.SaveCastPath);
    }

    private static void AddVideoArguments(VideoConfiguration? video, List<string> args)
    {
        if (video is null) return;
        Add(args, "--fps", video.Fps);
        Add(args, "--sleep", video.Sleep);
        Add(args, "--fadeout", video.FadeOut);
        Add(args, "--timing", video.Timing);
        Add(args, "--coalesce-ms", video.CoalesceMs);
        AddInverseFlag(args, "--no-loop", video.Loop);
    }

    private static void AddReplayArguments(ReplayConfiguration? replay, List<string> args)
    {
        if (replay is null) return;
        Add(args, "--replay", replay.Path);
        Add(args, "--replay-save", replay.SavePath);
    }

    private static void AddConversionArguments(ConversionConfiguration? conversion, List<string> args)
    {
        if (conversion is null) return;
        Add(args, "--svg-converter", conversion.SvgConverter);
    }

    private static void AddRuntimeArguments(RuntimeConfiguration? runtime, List<string> args)
    {
        if (runtime is null) return;
        AddFlag(args, "--verbose", runtime.Verbose);
        Add(args, "--verbose", runtime.VerboseLogPath);
        Add(args, "--timeout", runtime.Timeout);
        AddFlag(args, "--no-colorenv", runtime.NoColorEnv);
        AddFlag(args, "--no-delete-envs", runtime.NoDeleteEnvs);
    }

    private static void AddAppearanceArguments(AppearanceConfiguration? appearance, List<string> args)
    {
        if (appearance is null) return;
        Add(args, "--window", appearance.Window);
        Add(args, "--padding", appearance.Padding);
        AddFlag(args, "--pcmode", appearance.PcMode);
        Add(args, "--pc-padding", appearance.PcPadding);
        Add(args, "--opacity", appearance.Opacity);
        Add(args, "--theme", appearance.Theme);
        Add(args, "--forecolor", appearance.ForeColor);
        Add(args, "--backcolor", appearance.BackColor);
        if (appearance.Background is not null)
        {
            RemovePreviousArguments(args, "--background");
            foreach (var color in appearance.Background) Add(args, "--background", color, replace: false);
        }
        Add(args, "--font", appearance.Font);
        Add(args, "--fontsize", appearance.FontSize);
        Add(args, "--adjust", appearance.LengthAdjust);
        AddFlag(args, "--with-command", appearance.WithCommand);
        Add(args, "--header", appearance.Header);
        Add(args, "--prompt", appearance.Prompt);
    }

    private static void AddFlag(List<string> args, string option, bool? enabled)
    {
        if (!enabled.HasValue) return;
        RemovePreviousArguments(args, option);
        if (enabled.Value) args.Add(option);
    }

    private static void AddInverseFlag(List<string> args, string option, bool? enabled)
    {
        if (!enabled.HasValue) return;
        RemovePreviousArguments(args, option);
        if (!enabled.Value) args.Add(option);
    }

    private static void Add(List<string> args, string option, string? value, bool replace = true)
    {
        if (value is null) return;
        if (replace) RemovePreviousArguments(args, option);
        args.Add(option); args.Add(value);
    }

    private static void Add(List<string> args, string option, double? value)
    {
        if (value.HasValue) Add(args, option, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void RemovePreviousArguments(List<string> args, string option)
    {
        var takesValue = option is not "--pcmode" and not "--with-command";
        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(args[i], option, StringComparison.Ordinal)) continue;
            args.RemoveAt(i);
            if (takesValue && i < args.Count && !args[i].StartsWith("--", StringComparison.Ordinal)) args.RemoveAt(i);
        }
    }
}

[YamlObject]
internal sealed partial class ConsoleToSvgConfiguration
{
    public List<string>? Include { get; set; }
    public InputConfiguration? Input { get; set; }
    public OutputConfiguration? Output { get; set; }
    public CaptureConfiguration? Capture { get; set; }
    public VideoConfiguration? Video { get; set; }
    public ReplayConfiguration? Replay { get; set; }
    public ConversionConfiguration? Conversion { get; set; }
    public RuntimeConfiguration? Runtime { get; set; }
    public AppearanceConfiguration? Appearance { get; set; }
}

[YamlObject]
internal sealed partial class InputConfiguration { public string? CastPath { get; set; } }

[YamlObject]
internal sealed partial class OutputConfiguration
{
    public string? Path { get; set; }
    public string? Mode { get; set; }
    public bool? StdOut { get; set; }
    public string? SaveFramesDir { get; set; }
    public string? Size { get; set; }
}

[YamlObject]
internal sealed partial class CaptureConfiguration
{
    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? Frame { get; set; }
    public string? Time { get; set; }
    public string? CropTop { get; set; }
    public string? CropRight { get; set; }
    public string? CropBottom { get; set; }
    public string? CropLeft { get; set; }
    public string? SaveCastPath { get; set; }
}

[YamlObject]
internal sealed partial class VideoConfiguration
{
    public string? Fps { get; set; }
    public string? Sleep { get; set; }
    public string? FadeOut { get; set; }
    public string? Timing { get; set; }
    public string? CoalesceMs { get; set; }
    public bool? Loop { get; set; }
}

[YamlObject]
internal sealed partial class ReplayConfiguration { public string? Path { get; set; } public string? SavePath { get; set; } }

[YamlObject]
internal sealed partial class ConversionConfiguration { public string? SvgConverter { get; set; } }

[YamlObject]
internal sealed partial class RuntimeConfiguration
{
    public bool? Verbose { get; set; }
    public string? VerboseLogPath { get; set; }
    public string? Timeout { get; set; }
    public bool? NoColorEnv { get; set; }
    public bool? NoDeleteEnvs { get; set; }
}

[YamlObject]
internal sealed partial class AppearanceConfiguration
{
    public string? Window { get; set; }
    public double? Padding { get; set; }
    public bool? PcMode { get; set; }
    public double? PcPadding { get; set; }
    public double? Opacity { get; set; }
    public string? Theme { get; set; }
    public string? ForeColor { get; set; }
    public string? BackColor { get; set; }
    public List<string>? Background { get; set; }
    public string? Font { get; set; }
    public double? FontSize { get; set; }
    public string? LengthAdjust { get; set; }
    public bool? WithCommand { get; set; }
    public string? Header { get; set; }
    public string? Prompt { get; set; }
}
