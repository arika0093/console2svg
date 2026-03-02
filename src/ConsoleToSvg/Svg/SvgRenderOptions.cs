using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Svg;

public sealed class SvgRenderOptions
{
    public string Theme { get; set; } = "dark";

    public CropOptions Crop { get; set; } = CropOptions.Parse("0", "0", "0", "0");

    public int? Frame { get; set; }

    public string? Font { get; set; }

    public double FontSize { get; set; } = 14d;

    /// <summary>Window chrome definition. null = no chrome (transparent/plain).</summary>
    public ChromeDefinition? Chrome { get; set; }

    public double Padding { get; set; }

    public bool Loop { get; set; }

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 1d;

    public double VideoFadeOut { get; set; } = 0d;

    public double Opacity { get; set; } = 1d;

    public int? HeightRows { get; set; }

    public string? CommandHeader { get; set; }

    public string? ForeColor { get; set; }

    public string LengthAdjust { get; set; } = "spacing";

    /// <summary>背景指定: null=デフォルト、1要素=単色または画像パス、2要素=グラデーション</summary>
    public string[]? Background { get; set; }

    public static SvgRenderOptions FromAppOptions(AppOptions appOptions)
    {
        var chrome = ChromeLoader.Load(appOptions.Window);
        var effectivePadding = appOptions.Padding ?? (chrome != null ? 8d : 2d);
        var prompt = string.IsNullOrWhiteSpace(appOptions.Prompt) ? "$" : appOptions.Prompt;
        string? commandHeader = null;
        if (!string.IsNullOrWhiteSpace(appOptions.Header))
        {
            commandHeader = $"{prompt} {appOptions.Header}";
        }
        else if (appOptions.WithCommand && !string.IsNullOrWhiteSpace(appOptions.Command))
        {
            commandHeader = $"{prompt} {appOptions.Command}";
        }

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
            FontSize = appOptions.FontSize ?? 14d,
            Chrome = chrome,
            Padding = effectivePadding,
            Loop = appOptions.Loop,
            VideoFps = appOptions.VideoFps,
            VideoSleep = appOptions.VideoSleep,
            VideoFadeOut = appOptions.VideoFadeOut,
            HeightRows = appOptions.Height,
            Opacity = appOptions.Opacity,
            CommandHeader = commandHeader,
            ForeColor = appOptions.ForeColor,
            LengthAdjust = appOptions.LengthAdjust,
            Background = appOptions.Background.Count > 0 ? appOptions.Background.ToArray() : null,
        };
    }
}
