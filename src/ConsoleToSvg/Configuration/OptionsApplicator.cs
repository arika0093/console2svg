using System;
using System.Globalization;
using System.IO;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Configuration;

public static class OptionsApplicator
{
    public static void Apply(AppOptions target, ConsoleOptions source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        if (target.RequestedSessionAction is SessionAction.Start)
        {
            if (!target.IsSessionWidthExplicit && source.Terminal?.Width is { } sessionWidth)
            {
                target.SessionWidth = sessionWidth;
            }
            if (!target.IsSessionHeightExplicit && source.Terminal?.Height is { } sessionHeight)
            {
                target.SessionHeight = sessionHeight;
            }
        }
        else if (CanConfigurePty(target) && source.Terminal is { } terminal)
        {
            if (!target.IsWidthExplicit && target.Width is null)
            {
                target.Width = terminal.Width;
            }
            if (!target.IsHeightExplicit && target.Height is null)
            {
                target.Height = terminal.Height;
            }
        }

        if (CanLaunchProcess(target) && source.Environment is { } environment)
        {
            if (!target.IsNoColorEnvExplicit && environment.Color is { } color)
            {
                target.NoColorEnv = color == "preserve";
            }
            if (!target.IsNoDeleteEnvsExplicit && environment.Ci is { } ci)
            {
                target.NoDeleteEnvs = ci == "preserve";
            }
        }

        if (
            (
                target.Workflow is Workflow.Interactive or Workflow.LiveServer
                || target.Workflow == Workflow.Tmux
                    && target.RequestedTmuxAction == TmuxAction.LiveServer
                || target.Interactive
            )
            && !target.IsMouseExplicit
            && source.Interactive?.Mouse is { } mouse
        )
        {
            target.Mouse = mouse;
        }

        if (
            (
                target.Workflow == Workflow.LiveServer
                || target.Workflow == Workflow.Tmux
                    && target.RequestedTmuxAction == TmuxAction.LiveServer
            ) && !target.IsLiveServerEndpointExplicit
        )
        {
            ApplyLiveServerHost(target, source.LiveServer?.Host);
        }

        if (!CanConfigureCapture(target))
        {
            return;
        }

        ApplyCapture(target, source.Capture);
        ApplyRender(target, source.Render);
        ApplyAppearance(target, source.Appearance);
        ApplyConverter(target, source.Converter);
    }

    private static void ApplyCapture(AppOptions target, CaptureOptions? capture)
    {
        if (capture is null)
        {
            return;
        }

        if (!target.IsModeExplicit && capture.Mode is { } mode)
        {
            target.Mode = mode == "video" ? OutputMode.Video : OutputMode.Image;
        }

        if (capture.Image is { } image)
        {
            if (target.Frame is null)
            {
                target.Frame = image.Frame;
            }
            if (target.Time is null && target.TimeStart is null && target.TimeEnd is null)
            {
                target.Time = image.Time;
            }
        }

        if (capture.Video is { } video)
        {
            if (!target.IsVideoFpsExplicit && video.Fps is { } fps)
            {
                target.VideoFps = fps;
            }
            if (!target.IsLoopExplicit && video.Loop is { } loop)
            {
                target.Loop = loop;
            }
            if (!target.IsVideoTimingExplicit && video.Timing is { } timing)
            {
                target.VideoTiming =
                    timing == "realtime" ? VideoTimingMode.Realtime : VideoTimingMode.Deterministic;
            }
            if (!target.IsOutputCoalesceExplicit && video.Coalesce is { } coalesce)
            {
                target.OutputCoalesceMs =
                    coalesce == "auto"
                        ? null
                        : double.Parse(coalesce, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (!target.IsVideoFadeOutExplicit && video.Fadeout is { } fadeout)
            {
                target.VideoFadeOut = fadeout;
            }
            if (!target.IsTimeExplicit && target.Mode is OutputMode.Video && video.Time is { } time)
            {
                target.TimeStart = time.Start;
                target.TimeEnd = time.End;
            }
        }

        if (capture.Crop is { } crop)
        {
            if (!target.IsCropTopExplicit && crop.Top is { } top)
            {
                target.CropTop = top;
            }
            if (!target.IsCropRightExplicit && crop.Right is { } right)
            {
                target.CropRight = right;
            }
            if (!target.IsCropBottomExplicit && crop.Bottom is { } bottom)
            {
                target.CropBottom = bottom;
            }
            if (!target.IsCropLeftExplicit && crop.Left is { } left)
            {
                target.CropLeft = left;
            }
        }
    }

    private static void ApplyRender(AppOptions target, RenderOptions? render)
    {
        if (render is null)
        {
            return;
        }

        if (!target.IsLengthAdjustExplicit && render.Adjust is { } adjust)
        {
            target.LengthAdjust = adjust;
        }
        if (render.Masking is { } masking)
        {
            if (!target.IsMaskAutoExplicit && masking.Auto is { } auto)
            {
                target.MaskAuto = auto;
            }
            if (!target.IsMaskPatternsExplicit && masking.Strings is { } patterns)
            {
                target.MaskPatterns.AddRange(patterns);
                target.IsMaskPatternsExplicit = true;
            }
        }
    }

    private static void ApplyAppearance(AppOptions target, AppearanceOptions? appearance)
    {
        if (appearance is null)
        {
            return;
        }

        if (!target.IsThemesExplicit && appearance.Theme is { } themes && target.Themes.Count == 0)
        {
            target.Themes.AddRange(themes);
            target.IsThemesExplicit = true;
        }
        if (!target.IsWindowExplicit && appearance.Window is { } window)
        {
            target.Window = window;
            target.IsWindowExplicit = true;
        }
        if (!target.IsFontExplicit && appearance.Font?.Family is { } font)
        {
            target.Font = font;
            target.IsFontExplicit = true;
        }
        if (!target.IsFontSizeExplicit && appearance.Font?.Size is { } fontSize)
        {
            target.FontSize = fontSize;
            target.IsFontSizeExplicit = true;
        }
        if (!target.IsPaddingExplicit && appearance.Padding is { } padding)
        {
            target.Padding = padding;
            target.IsPaddingExplicit = true;
        }
        if (!target.IsMarginExplicit && appearance.Margin is { } margin)
        {
            target.Margin = margin;
            target.IsMarginExplicit = true;
        }
        if (!target.IsOpacityExplicit && appearance.Opacity is { } opacity)
        {
            target.Opacity = opacity;
            target.IsOpacityExplicit = true;
        }
        if (appearance.PcPadding is { } pcPadding)
        {
            target.PcPadding ??= pcPadding;
        }
        if (!target.IsBackgroundExplicit && appearance.Background is { } background)
        {
            target.Background.AddRange(background);
            target.IsBackgroundExplicit = true;
        }
        if (!target.IsForeColorExplicit && appearance.Forecolor is { } forecolor)
        {
            target.ForeColor = forecolor;
            target.IsForeColorExplicit = true;
        }
        if (!target.IsBackColorExplicit && appearance.Backcolor is { } backcolor)
        {
            target.BackColor = backcolor;
            target.IsBackColorExplicit = true;
        }
        if (!target.IsHeaderExplicit && appearance.Header?.Text is { } text)
        {
            target.Header = text;
        }
        if (!target.IsWithCommandExplicit && appearance.Header?.WithCommand is { } withCommand)
        {
            target.WithCommand = withCommand;
        }
        if (!target.IsPromptExplicit && appearance.Header?.Prompt is { } prompt)
        {
            target.Prompt = prompt;
        }

        if (appearance.Size is { } size)
        {
            if (!target.IsSizeWidthExplicit)
            {
                target.SizeWidth ??= size.Width;
            }
            if (!target.IsSizeHeightExplicit)
            {
                target.SizeHeight ??= size.Height;
            }
        }
    }

    private static void ApplyConverter(AppOptions target, ConverterOptions? converter)
    {
        if (!target.IsSvgConverterExplicit && converter?.SvgConverter is { } svgConverter)
        {
            target.SvgConverter = svgConverter switch
            {
                "ffmpeg" => SvgConverterMode.Ffmpeg,
                "rsvg" or "rsvg-convert" => SvgConverterMode.RsvgConvert,
                "resvg" => SvgConverterMode.Resvg,
                _ => SvgConverterMode.Auto,
            };
        }
    }

    private static void ApplyLiveServerHost(AppOptions target, string? endpoint)
    {
        if (endpoint is null)
        {
            return;
        }

        var separator = endpoint.StartsWith("[", StringComparison.Ordinal)
            ? endpoint.IndexOf("]:", StringComparison.Ordinal)
            : endpoint.LastIndexOf(':');
        if (separator < 0)
        {
            target.ListenAddress = endpoint;
            return;
        }

        var host = endpoint.StartsWith("[", StringComparison.Ordinal)
            ? endpoint[1..separator]
            : endpoint[..separator];
        var portText = endpoint[
            (separator + (endpoint.StartsWith("[", StringComparison.Ordinal) ? 2 : 1))..
        ];
        if (
            !int.TryParse(
                portText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var port
            ) || port is < 1 or > 65535
        )
        {
            throw new InvalidDataException($"Invalid live-server endpoint '{endpoint}'.");
        }

        target.ListenAddress = string.IsNullOrWhiteSpace(host) ? null : host;
        target.LiveServerPort = port;
    }

    private static bool CanConfigureCapture(AppOptions options) =>
        options.Workflow
            is Workflow.Capture
                or Workflow.Legacy
                or Workflow.Replay
                or Workflow.Cast
                or Workflow.Interactive
                or Workflow.LiveServer
                or Workflow.Tmux
                or Workflow.Batch
        || options.Workflow == Workflow.Session
            && options.RequestedSessionAction is SessionAction.Capture or SessionAction.Inspect;

    private static bool CanConfigurePty(AppOptions options) =>
        options.InputCastPath is null
        && options.Workflow
            is Workflow.Capture
                or Workflow.Legacy
                or Workflow.Replay
                or Workflow.Interactive
                or Workflow.LiveServer;

    private static bool CanLaunchProcess(AppOptions options) =>
        options.Workflow
            is Workflow.Capture
                or Workflow.Legacy
                or Workflow.Replay
                or Workflow.Interactive
                or Workflow.LiveServer
                or Workflow.Tmux
                or Workflow.Batch
        || options.Workflow == Workflow.Session
            && options.RequestedSessionAction == SessionAction.Start;
}
