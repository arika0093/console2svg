using System;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

/// <summary>Maps the reusable persisted session model onto runtime CLI options.</summary>
internal static class SessionOptionsOverlay
{
    internal static void Apply(AppOptions target, SessionOptions source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        if (!target.IsCliOptionExplicit("width") && source.Terminal.Width is int width)
        {
            target.Width = width;
            target.WidthAdjust = false;
        }
        if (!target.IsCliOptionExplicit("height") && source.Terminal.Height is int height)
        {
            target.Height = height;
            target.HeightAdjust = false;
        }

        var appearance = source.Appearance;
        if (!target.IsCliOptionExplicit("theme") && !string.IsNullOrWhiteSpace(appearance.Theme))
        {
            target.Themes.Clear();
            target.Themes.Add(appearance.Theme);
        }
        if (!target.IsCliOptionExplicit("font") && appearance.Font is not null)
        {
            target.Font = appearance.Font;
        }
        if (!target.IsCliOptionExplicit("fontSize") && appearance.FontSize is not null)
        {
            target.FontSize = appearance.FontSize;
        }
        if (!target.IsCliOptionExplicit("window") && appearance.Window is not null)
        {
            target.Window = appearance.Window;
        }
        if (!target.IsCliOptionExplicit("margin") && appearance.Margin is not null)
        {
            target.Margin = appearance.Margin;
        }
        if (!target.IsCliOptionExplicit("padding") && appearance.Padding is not null)
        {
            target.Padding = appearance.Padding;
        }
        if (!target.IsCliOptionExplicit("foreColor") && appearance.ForeColor is not null)
        {
            target.ForeColor = appearance.ForeColor;
        }
        if (!target.IsCliOptionExplicit("backColor") && appearance.BackColor is not null)
        {
            target.BackColor = appearance.BackColor;
        }
        if (!target.IsCliOptionExplicit("background") && appearance.Background is not null)
        {
            target.Background.Clear();
            target.Background.AddRange(appearance.Background);
        }

        var render = source.Render;
        if (!target.IsCliOptionExplicit("mode") && render.Mode is not null)
        {
            target.Mode = ParseMode(render.Mode);
        }
        if (!target.IsCliOptionExplicit("fps") && render.Fps is not null)
        {
            target.VideoFps = render.Fps.Value;
        }
        if (!target.IsCliOptionExplicit("timing") && render.Timing is not null)
        {
            target.VideoTiming = ParseTiming(render.Timing);
        }
        if (!target.IsCliOptionExplicit("loop") && render.Loop is not null)
        {
            target.Loop = render.Loop.Value;
        }
        if (!target.IsCliOptionExplicit("sleep") && render.Sleep is not null)
        {
            target.VideoSleep = render.Sleep.Value;
        }
        if (!target.IsCliOptionExplicit("fadeOut") && render.FadeOut is not null)
        {
            target.VideoFadeOut = render.FadeOut.Value;
        }
        if (!target.IsCliOptionExplicit("coalesce") && render.OutputCoalesceMs is not null)
        {
            target.OutputCoalesceMs = render.OutputCoalesceMs;
        }
    }

    internal static SessionOptions Snapshot(AppOptions source) =>
        new()
        {
            Terminal = new TerminalSessionOptions { Width = source.Width, Height = source.Height },
            Appearance = new AppearanceSessionOptions
            {
                Theme = source.Theme,
                Font = source.Font,
                FontSize = source.FontSize,
                Window = source.Window,
                Margin = source.Margin,
                Padding = source.Padding,
                ForeColor = source.ForeColor,
                BackColor = source.BackColor,
                Background = source.Background.Count == 0 ? null : [.. source.Background],
            },
            Render = new RenderSessionOptions
            {
                Mode = source.Mode.ToString().ToLowerInvariant(),
                Fps = source.VideoFps,
                Timing = source.VideoTiming.ToString().ToLowerInvariant(),
                Loop = source.Loop,
                Sleep = source.VideoSleep,
                FadeOut = source.VideoFadeOut,
                OutputCoalesceMs = source.OutputCoalesceMs,
            },
        };

    private static OutputMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "image" => OutputMode.Image,
            "video" => OutputMode.Video,
            _ => throw new FormatException("options.render.mode must be 'image' or 'video'."),
        };

    private static VideoTimingMode ParseTiming(string value) =>
        value.ToLowerInvariant() switch
        {
            "deterministic" => VideoTimingMode.Deterministic,
            "realtime" => VideoTimingMode.Realtime,
            _ => throw new FormatException(
                "options.render.timing must be 'deterministic' or 'realtime'."
            ),
        };
}
