using System;
using System.Linq;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Cli;

public static class SvgRenderOptionsFactory
{
    public static SvgRenderOptions Create(AppOptions appOptions)
    {
        var catalog = new ThemeCatalog();
        var themeIds = appOptions.Themes.Count == 0 ? ["dark"] : appOptions.Themes;
        var themeEntries = themeIds.Select(catalog.Resolve).ToArray();
        var appearances = themeEntries
            .Select(entry =>
                entry.IsPcVariant
                    ? entry.Manifest.Appearance?.Pc
                    : entry.Manifest.Appearance?.Normal
            )
            .Where(x => x is not null)
            .ToArray();
        var selectedTheme = themeEntries.LastOrDefault();
        var windowName = appOptions.IsWindowExplicit
            ? appOptions.Window
            : appearances.LastOrDefault(x => x is not null && x.Window is not null)?.Window
                ?? appearances.LastOrDefault(x => x is not null)?.Window
                ?? "none";
        var chromeTheme = appOptions.IsWindowExplicit ? catalog.Resolve(windowName) : selectedTheme;
        var chromeAppearance =
            chromeTheme?.IsPcVariant == true
                ? chromeTheme.Manifest.Appearance?.Pc
                : chromeTheme?.Manifest.Appearance?.Normal;
        var chromePath = appOptions.IsWindowExplicit ? chromeAppearance?.Window : windowName;
        var chrome =
            chromeTheme is not null && !string.IsNullOrWhiteSpace(chromePath)
                ? ThemeCatalog.ResolveChrome(chromeTheme, chromePath)
                : null;
        if (
            chrome is not null
            && (themeEntries.Any(x => x.IsPcVariant) || chromeTheme?.IsPcVariant == true)
        )
            chrome.IsDesktop = true;
        var pcPadding =
            appOptions.PcPadding
            ?? appearances.LastOrDefault(x => x!.PcPadding.HasValue)?.PcPadding
            ?? chromeAppearance?.PcPadding;
        if (chrome is not null && pcPadding.HasValue)
            chrome.DesktopPadding = pcPadding.Value;

        var prompt = string.IsNullOrWhiteSpace(appOptions.Prompt) ? "$" : appOptions.Prompt;
        string? commandHeader = null;
        if (!string.IsNullOrWhiteSpace(appOptions.Header))
            commandHeader = $"{prompt} {appOptions.Header}";
        else if (appOptions.WithCommand && !string.IsNullOrWhiteSpace(appOptions.Command))
            commandHeader = $"{prompt} {appOptions.Command}";

        return new SvgRenderOptions
        {
            Theme = appOptions.Theme,
            TerminalTheme = Theme.ResolveMany(themeIds),
            Crop = CropOptions.Parse(
                appOptions.CropTop,
                appOptions.CropRight,
                appOptions.CropBottom,
                appOptions.CropLeft
            ),
            Frame = appOptions.Frame,
            Time = appOptions.Time,
            TimeStart = appOptions.TimeStart,
            TimeEnd = appOptions.TimeEnd,
            Font = appOptions.IsFontExplicit
                ? appOptions.Font
                : appearances.LastOrDefault(x => x!.Font is not null)?.Font,
            FontSize = appOptions.IsFontSizeExplicit
                ? appOptions.FontSize ?? 14d
                : appearances.LastOrDefault(x => x!.FontSize.HasValue)?.FontSize ?? 14d,
            Chrome = chrome,
            Margin = appOptions.IsMarginExplicit
                ? appOptions.Margin ?? 0d
                : appearances.LastOrDefault(x => x!.Margin.HasValue)?.Margin ?? 0d,
            Padding = appOptions.IsPaddingExplicit
                ? appOptions.Padding ?? 8d
                : appearances.LastOrDefault(x => x!.Padding.HasValue)?.Padding ?? 8d,
            Loop = appOptions.Loop,
            VideoFps = appOptions.VideoFps,
            VideoTiming = appOptions.VideoTiming,
            VideoSleep = appOptions.VideoSleep,
            VideoFadeOut = appOptions.VideoFadeOut,
            RenderCursor = appOptions.Mode is OutputMode.Video or OutputMode.Repeat,
            HeightRows = appOptions.Height,
            Opacity = appOptions.IsOpacityExplicit
                ? appOptions.Opacity
                : appearances.LastOrDefault(x => x!.Opacity.HasValue)?.Opacity ?? 1d,
            CommandHeader = commandHeader,
            ForeColor = appOptions.ForeColor,
            LengthAdjust = appOptions.LengthAdjust,
            Background = appOptions.IsBackgroundExplicit
                ? appOptions.Background.ToArray()
                : ResolveThemeBackground(themeEntries, appearances),
            BackColor = appOptions.BackColor,
            SizeWidth = appOptions.SizeWidth,
            SizeHeight = appOptions.SizeHeight,
            MaskPatterns =
                appOptions.MaskPatterns.Count > 0 ? appOptions.MaskPatterns.ToArray() : null,
        };
    }

    private static string[]? ResolveThemeBackground(
        ThemeEntry[] entries,
        ThemeVariant?[] appearances
    )
    {
        for (var index = appearances.Length - 1; index >= 0; index--)
            if (appearances[index]?.Background is { } background)
                return ThemeCatalog.ResolveBackground(entries[index], background);
        return null;
    }
}
