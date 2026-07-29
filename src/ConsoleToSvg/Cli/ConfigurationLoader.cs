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
        AddFlag(args, "--verbose", config.Verbose);
        Add(args, "--verbose", config.VerboseLogPath);
        AddFlag(args, "--version", config.ShowVersion);
        AddFlag(args, "--install-deps", config.InstallDependencies);
        Add(args, "--in", config.InputCastPath);
        Add(args, "--out", config.OutputPath);
        Add(args, "--mode", config.Mode);
        Add(args, "--width", config.Width);
        Add(args, "--height", config.Height);
        Add(args, "--frame", config.Frame);
        Add(args, "--time", config.Time);
        Add(args, "--crop-top", config.CropTop);
        Add(args, "--crop-right", config.CropRight);
        Add(args, "--crop-bottom", config.CropBottom);
        Add(args, "--crop-left", config.CropLeft);
        Add(args, "--save-cast", config.SaveCastPath);
        Add(args, "--replay-save", config.ReplaySavePath);
        Add(args, "--replay", config.ReplayPath);
        AddFlag(args, "--no-colorenv", config.NoColorEnv);
        AddFlag(args, "--no-delete-envs", config.NoDeleteEnvs);
        Add(args, "--fps", config.VideoFps);
        Add(args, "--sleep", config.VideoSleep);
        Add(args, "--fadeout", config.VideoFadeOut);
        Add(args, "--timing", config.VideoTiming);
        Add(args, "--coalesce-ms", config.OutputCoalesceMs);
        Add(args, "--timeout", config.Timeout);
        AddFlag(args, "--stdout", config.StdOut);
        AddFlag(args, "--interactive", config.Interactive);
        Add(args, "--save-frames", config.SaveFramesDir);
        Add(args, "--size", config.Size);
        Add(args, "--svg-converter", config.SvgConverter);
        AddInverseFlag(args, "--no-loop", config.Loop);

        AddAppearanceArguments(config.Appearance, args);
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
    public AppearanceConfiguration? Appearance { get; set; }
    public bool? Verbose { get; set; }
    public string? VerboseLogPath { get; set; }
    public bool? ShowVersion { get; set; }
    public bool? InstallDependencies { get; set; }
    public string? Command { get; set; }
    public string? InputCastPath { get; set; }
    public string? OutputPath { get; set; }
    public string? Mode { get; set; }
    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? Frame { get; set; }
    public string? Time { get; set; }
    public string? CropTop { get; set; }
    public string? CropRight { get; set; }
    public string? CropBottom { get; set; }
    public string? CropLeft { get; set; }
    public string? SaveCastPath { get; set; }
    public string? ReplaySavePath { get; set; }
    public string? ReplayPath { get; set; }
    public bool? NoColorEnv { get; set; }
    public bool? NoDeleteEnvs { get; set; }
    public string? VideoFps { get; set; }
    public string? VideoSleep { get; set; }
    public string? VideoFadeOut { get; set; }
    public string? VideoTiming { get; set; }
    public string? OutputCoalesceMs { get; set; }
    public string? Timeout { get; set; }
    public bool? Loop { get; set; }
    public bool? StdOut { get; set; }
    public bool? Interactive { get; set; }
    public string? SaveFramesDir { get; set; }
    public string? Size { get; set; }
    public string? SvgConverter { get; set; }
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
