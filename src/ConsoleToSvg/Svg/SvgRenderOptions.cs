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

    public double Opacity { get; set; } = 1d;

    public int? HeightRows { get; set; }

    public string? CommandHeader { get; set; }

    /// <summary>背景指定: null=デフォルト、1要素=単色または画像パス、2要素=グラデーション</summary>
    public string[]? Background { get; set; }

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
            Opacity = appOptions.Opacity,
            CommandHeader = (appOptions.WithCommand && !string.IsNullOrWhiteSpace(appOptions.Command))
                ? $"$ {appOptions.Command}"
                : null,
            Background = appOptions.Background.Count > 0 ? appOptions.Background.ToArray() : null,
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
