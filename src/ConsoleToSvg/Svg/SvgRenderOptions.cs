using System;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Svg;

public enum WindowStyle
{
    None,
    Macos,
    Windows,
}

public sealed class SvgRenderOptions
{
    public string Theme { get; set; } = "dark";

    public CropOptions Crop { get; set; } = CropOptions.Parse("0", "0", "0", "0");

    public int? Frame { get; set; }

    public string? Font { get; set; }

    public WindowStyle Window { get; set; } = WindowStyle.None;

    public double Padding { get; set; }

    public static SvgRenderOptions FromAppOptions(AppOptions appOptions)
    {
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
            Window = ParseWindowStyle(appOptions.Window),
            Padding = appOptions.Padding,
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

        return WindowStyle.None;
    }
}
