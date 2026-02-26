using System;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Svg;

public enum WindowStyle
{
    None,
    Macos,
    Windows,
    MacosPc,
    WindowsPc,
}

public sealed class SvgRenderOptions
{
    public string Theme { get; set; } = "dark";

    public CropOptions Crop { get; set; } = CropOptions.Parse("0", "0", "0", "0");

    public int? Frame { get; set; }

    public string? Font { get; set; }

    public WindowStyle Window { get; set; } = WindowStyle.None;

    public double Padding { get; set; }

    public bool Loop { get; set; }

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 1d;

    public double VideoFadeOut { get; set; } = 0d;

    public int? HeightRows { get; set; }

    public string? CommandHeader { get; set; }

    public static SvgRenderOptions FromAppOptions(AppOptions appOptions)
    {
        var windowStyle = ParseWindowStyle(appOptions.Window);
        var effectivePadding = appOptions.Padding
            ?? (windowStyle != WindowStyle.None ? 8d : 2d);

        return new SvgRenderOptions
        {
            Theme = appOptions.Theme,
            Crop = CropOptions.Parse(
                appOptions.CropTop,
                appOptions.CropRight,
                appOptions.CropBottom,
                appOptions.CropLeft
            ),
            Frame = appOptions.Frame,
            Font = appOptions.Font,
            Window = windowStyle,
            Padding = effectivePadding,
            Loop = appOptions.Loop,
            VideoFps = appOptions.VideoFps,
            VideoSleep = appOptions.VideoSleep,
            VideoFadeOut = appOptions.VideoFadeOut,
            HeightRows = appOptions.Height,
            CommandHeader = (appOptions.WithCommand && !string.IsNullOrWhiteSpace(appOptions.Command))
                ? $"$ {appOptions.Command}"
                : null,
        };
    }

    private static WindowStyle ParseWindowStyle(string? value)
    {
        if (string.Equals(value, "macos", StringComparison.OrdinalIgnoreCase))
        {
            return WindowStyle.Macos;
        }

        if (string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
        {
            return WindowStyle.Windows;
        }

        if (string.Equals(value, "macos-pc", StringComparison.OrdinalIgnoreCase))
        {
            return WindowStyle.MacosPc;
        }

        if (string.Equals(value, "windows-pc", StringComparison.OrdinalIgnoreCase))
        {
            return WindowStyle.WindowsPc;
        }

        return WindowStyle.None;
    }
}
