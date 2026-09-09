using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public sealed partial class ConsoleToSvgCommandLine
{
    private void SetMappedAction(
        Command command,
        Workflow workflow,
        Argument<string[]>? commandArgument = null,
        Argument<string>? inputPath = null,
        Argument<string>? replayPath = null,
        Argument<string?>? portArgument = null,
        ThemeAction? themeAction = null,
        Argument<string>? themeArgument = null,
        Argument<string?>? optionalThemeArgument = null,
        TmuxAction? tmuxAction = null
    )
    {
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (
                    !TryCreateOptions(
                        parseResult,
                        workflow,
                        commandArgument,
                        inputPath,
                        replayPath,
                        portArgument,
                        themeAction,
                        themeArgument,
                        optionalThemeArgument,
                        tmuxAction,
                        out var options,
                        out var error
                    )
                )
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(error);
                    parseResult.InvocationConfiguration.Error.WriteLine();
                    ColoredHelpAction.Write(parseResult);
                    return 1;
                }

                return await _handler(options!, parseResult, cancellationToken)
                    .ConfigureAwait(false);
            }
        );
    }

    private bool TryCreateOptions(
        ParseResult result,
        Workflow workflow,
        Argument<string[]>? commandArgument,
        Argument<string>? inputPath,
        Argument<string>? replayPath,
        Argument<string?>? portArgument,
        ThemeAction? themeAction,
        Argument<string>? themeArgument,
        Argument<string?>? optionalThemeArgument,
        TmuxAction? tmuxAction,
        out AppOptions? options,
        out string? error
    )
    {
        options = new AppOptions
        {
            Workflow = result.GetValue(_symbols.LegacyRoot) ? Workflow.Legacy : workflow,
            OutputPath = result.GetValue(_symbols.OutputPath)?.ToString() ?? "output.svg",
            InputCastPath = result.GetValue(_symbols.InputCastPath),
            Verbose = IsSpecified(result, _symbols.Verbose),
            VerboseLogPath = result.GetValue(_symbols.VerboseLogPath)?.ToString(),
            WithCommand = result.GetValue(_symbols.WithCommand),
            StdOut = result.GetValue(_symbols.StdOut),
            Interactive = workflow == Workflow.Interactive || result.GetValue(_symbols.Interactive),
            NoColorEnv = result.GetValue(_symbols.NoColorEnv),
            NoDeleteEnvs = result.GetValue(_symbols.NoDeleteEnvs),
            Loop = !result.GetValue(_symbols.NoLoop),
            LiveServerResize = !result.GetValue(_symbols.NoResize),
            EmbedCast = result.GetValue(_symbols.EmbedCast),
            EmbedLogs = result.GetValue(_symbols.EmbedLogs),
            EmbedReplay = result.GetValue(_symbols.EmbedReplay),
            EmbedDebug = result.GetValue(_symbols.EmbedDebug),
            TmuxTarget = result.GetValue(_symbols.TmuxTarget),
            ListenAddress = result.GetValue(_symbols.ListenAddress),
            Font = result.GetValue(_symbols.Font),
            ForeColor = result.GetValue(_symbols.ForeColor),
            BackColor = result.GetValue(_symbols.BackColor),
            Header = result.GetValue(_symbols.Header),
            Prompt = result.GetValue(_symbols.Prompt),
            SaveCastPath = result.GetValue(_symbols.SaveCastPath),
            ReplaySavePath = result.GetValue(_symbols.ReplaySavePath),
            ReplayPath = result.GetValue(_symbols.ReplayPath),
            SaveFramesDir = result.GetValue(_symbols.SaveFramesPath),
            CropTop = result.GetValue(_symbols.CropTop) ?? "0",
            CropRight = result.GetValue(_symbols.CropRight) ?? "0",
            CropBottom = result.GetValue(_symbols.CropBottom) ?? "0",
            CropLeft = result.GetValue(_symbols.CropLeft) ?? "0",
            VideoFps = result.GetValue(_symbols.Fps) ?? 12d,
            VideoSleep = result.GetValue(_symbols.Sleep) ?? 0d,
            VideoFadeOut = result.GetValue(_symbols.FadeOut) ?? 0d,
            Opacity = result.GetValue(_symbols.Opacity) ?? 1d,
            Margin = result.GetValue(_symbols.Margin),
            Padding = result.GetValue(_symbols.Padding),
            FontSize = result.GetValue(_symbols.FontSize),
            PcPadding = result.GetValue(_symbols.PcPadding),
            Frame = result.GetValue(_symbols.Frame),
            RequestedThemeAction = themeAction,
            RequestedTmuxAction = tmuxAction,
        };

        if (options.EmbedDebug)
        {
            options.EmbedCast = true;
            options.EmbedLogs = true;
            options.EmbedReplay = true;
        }

        var mode = result.GetValue(_symbols.Mode);
        if (IsVideoMoreRecent(result))
        {
            options.Mode = OutputMode.Video;
            options.IsModeExplicit = true;
        }
        else if (mode.HasValue)
        {
            options.Mode = mode.Value;
            options.IsModeExplicit = true;
        }

        ApplyDimension(result.GetValue(_symbols.Width), isWidth: true, options);
        ApplyDimension(result.GetValue(_symbols.Height), isWidth: false, options);
        ApplyTime(result.GetValue(_symbols.Time), options);
        ApplySize(result.GetValue(_symbols.Size), options);
        options.OutputCoalesceMs = ParseCoalesce(result.GetValue(_symbols.Coalesce));
        options.VideoTiming = result.GetValue(_symbols.Timing) ?? VideoTimingMode.Deterministic;
        options.LengthAdjust = result.GetValue(_symbols.Adjust) ?? "spacing";
        options.SvgConverter = ParseSvgConverter(result.GetValue(_symbols.SvgConverter));

        if (IsSpecified(result, _symbols.Window))
        {
            options.Window = result.GetValue(_symbols.Window) ?? "macos";
            options.IsWindowExplicit = true;
        }
        options.IsForeColorExplicit = IsSpecified(result, _symbols.ForeColor);
        options.IsBackColorExplicit = IsSpecified(result, _symbols.BackColor);
        options.IsFontExplicit = IsSpecified(result, _symbols.Font);
        options.IsFontSizeExplicit = IsSpecified(result, _symbols.FontSize);
        options.IsMarginExplicit = IsSpecified(result, _symbols.Margin);
        options.IsPaddingExplicit = IsSpecified(result, _symbols.Padding);
        options.IsOpacityExplicit = IsSpecified(result, _symbols.Opacity);

        var themes = result.GetValue(_symbols.Theme);
        if (themes is not null)
            options.Themes.AddRange(themes);
        options.MaskPatterns.AddRange(result.GetValue(_symbols.Mask) ?? []);
        if (
            !ApplyBackground(
                result.GetValue(_symbols.Background)?.Select(value => value.ToString()).ToArray(),
                options,
                out error
            )
        )
        {
            options = null;
            return false;
        }

        options.TmuxHistory = IsSpecified(result, _symbols.History);
        options.TmuxHistoryLines = result.GetValue(_symbols.History);
        options.Timeout = result.GetValue(_symbols.Timeout);
        options.LiveServerPort = 38473;
        if (!ApplyPort(portArgument, result, options, out var leadingCommand, out error))
        {
            options = null;
            return false;
        }

        if (inputPath is not null)
            options.InputCastPath = result.GetRequiredValue(inputPath);
        if (replayPath is not null)
            options.ReplayPath = result.GetRequiredValue(replayPath);
        if (themeArgument is not null)
            options.ThemeArgument = result.GetRequiredValue(themeArgument);
        if (optionalThemeArgument is not null)
            options.ThemeArgument = result.GetValue(optionalThemeArgument);

        SetCommand(
            options,
            leadingCommand,
            commandArgument is null ? null : result.GetValue(commandArgument),
            result.Tokens.Any(token => token.Type == TokenType.DoubleDash)
        );
        if (!ValidateCrossOptions(options, out error))
        {
            options = null;
            return false;
        }

        return true;
    }

    private static bool IsSpecified(ParseResult result, Option option) =>
        result.GetResult(option) is { Implicit: false };

    private static bool IsVideoMoreRecent(ParseResult result)
    {
        for (var index = result.Tokens.Count - 1; index >= 0; index--)
        {
            if (result.Tokens[index].Type != TokenType.Option)
                continue;

            var value = result.Tokens[index].Value;
            if (value is "--video" or "-v")
                return true;
            if (value is "--mode" or "-m")
                return false;
        }
        return false;
    }

    private static void ApplyDimension(string? value, bool isWidth, AppOptions options)
    {
        if (value is null || string.Equals(value, "adjust", StringComparison.OrdinalIgnoreCase))
            return;

        var parsed = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (isWidth)
        {
            options.Width = parsed;
            options.WidthAdjust = false;
        }
        else
        {
            options.Height = parsed;
            options.HeightAdjust = false;
        }
    }

    private static void ApplyTime(string? value, AppOptions options)
    {
        if (value is null)
            return;

        var dashIndex = value.IndexOf('-');
        if (dashIndex > 0 && dashIndex < value.Length - 1)
        {
            options.TimeStart = double.Parse(
                value[..dashIndex],
                NumberStyles.Float,
                CultureInfo.InvariantCulture
            );
            options.TimeEnd = double.Parse(
                value[(dashIndex + 1)..],
                NumberStyles.Float,
                CultureInfo.InvariantCulture
            );
            return;
        }

        options.Time = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static void ApplySize(string? value, AppOptions options)
    {
        if (value is null)
            return;

        var separator = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            options.SizeWidth = double.Parse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture
            );
            return;
        }

        var width = value[..separator];
        var height = value[(separator + 1)..];
        if (!string.IsNullOrEmpty(width) && width != "*")
            options.SizeWidth = double.Parse(
                width,
                NumberStyles.Float,
                CultureInfo.InvariantCulture
            );
        if (!string.IsNullOrEmpty(height) && height != "*")
            options.SizeHeight = double.Parse(
                height,
                NumberStyles.Float,
                CultureInfo.InvariantCulture
            );
    }

    private static double? ParseCoalesce(string? value) =>
        value is null || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static SvgConverterMode ParseSvgConverter(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "ffmpeg" => SvgConverterMode.Ffmpeg,
            "rsvg" or "rsvg-convert" => SvgConverterMode.RsvgConvert,
            "resvg" => SvgConverterMode.Resvg,
            _ => SvgConverterMode.Auto,
        };

    private static bool ApplyBackground(
        string[]? backgroundValues,
        AppOptions options,
        out string? error
    )
    {
        error = null;
        if (backgroundValues is null || backgroundValues.Length == 0)
            return true;

        options.IsBackgroundExplicit = true;
        foreach (var value in backgroundValues)
        {
            if (
                options.Background.Count == 0
                && value.Contains(':', StringComparison.Ordinal)
                && !value.Contains("://", StringComparison.Ordinal)
            )
            {
                var separator = value.IndexOf(':', StringComparison.Ordinal);
                var start = value[..separator];
                var end = value[(separator + 1)..];
                if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
                {
                    options.Background.Add(start);
                    options.Background.Add(end);
                    continue;
                }
            }

            options.Background.Add(value);
        }

        if (options.Background.Count > 2)
        {
            error = "--background can be specified at most twice (start color and end color).";
            return false;
        }
        return true;
    }

    private static bool ApplyPort(
        Argument<string?>? portArgument,
        ParseResult result,
        AppOptions options,
        out string? leadingCommand,
        out string? error
    )
    {
        leadingCommand = null;
        error = null;
        if (portArgument is null)
            return true;

        var portText = result.GetValue(portArgument);
        if (string.IsNullOrEmpty(portText))
            return true;

        if (
            int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
        )
        {
            options.LiveServerPort = port;
            return true;
        }

        if (TryParseHostPort(portText, out var host, out port))
        {
            options.ListenAddress = host;
            options.LiveServerPort = port;
            return true;
        }

        if (options.Workflow == Workflow.Tmux)
        {
            error = "tmux live-server port must be an integer.";
            return false;
        }

        leadingCommand = portText;
        return true;
    }

    private static bool TryParseHostPort(string value, out string? host, out int port)
    {
        host = null;
        port = 0;

        var isBracketedHost = value.StartsWith("[", StringComparison.Ordinal);
        var portStart = isBracketedHost
            ? value.IndexOf("]:".AsSpan(), StringComparison.Ordinal)
            : value.LastIndexOf(':');
        if (portStart < 0)
            return false;

        if (isBracketedHost)
        {
            host = value[1..portStart];
            portStart += 2;
        }
        else
        {
            host = value[..portStart];
            portStart++;
        }

        if (string.IsNullOrWhiteSpace(host))
            host = null;
        if (portStart >= value.Length)
            return false;

        return int.TryParse(
            value[portStart..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out port
        );
    }

    private static void SetCommand(
        AppOptions options,
        string? leadingCommand,
        string[]? commandTokens,
        bool hasDoubleDash
    )
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(leadingCommand))
            values.Add(leadingCommand);
        if (commandTokens is not null)
            values.AddRange(commandTokens);

        if (values.Count == 0)
            return;

        options.Command = string.Join(' ', values);
        if (hasDoubleDash)
            options.DelimitedCommand = values.ToArray();
    }

    private static bool ValidateCrossOptions(AppOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            error = "--out must not be empty.";
            return false;
        }

        if (
            options.Workflow != Workflow.Tmux
            && (!string.IsNullOrWhiteSpace(options.TmuxTarget) || options.TmuxHistory)
        )
        {
            error = "--target and --history are only available with console2svg tmux.";
            return false;
        }

        if (
            options.Workflow == Workflow.Tmux
            && (
                options.RequestedTmuxAction is null
                || !string.IsNullOrWhiteSpace(options.Command)
                || !string.IsNullOrWhiteSpace(options.InputCastPath)
                || options.Interactive
            )
        )
        {
            error = "tmux workflows do not accept a command, --in, or --interactive.";
            return false;
        }

        if (
            options.Frame.HasValue
            && (options.Time.HasValue || options.TimeStart.HasValue || options.TimeEnd.HasValue)
        )
        {
            error = "--time and --frame are mutually exclusive.";
            return false;
        }

        if (options.Interactive)
        {
            if (options.EmbedCast)
            {
                error = "--interactive cannot be used with --embed-cast.";
                return false;
            }
            if (options.EmbedLogs || options.EmbedReplay)
            {
                error = "--interactive cannot be used with embed options.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(options.Command) && options.DelimitedCommand is null)
            {
                error =
                    "An interactive program must be specified after -- (for example: -i -- vim).";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(options.InputCastPath))
            {
                error = "--interactive cannot be used with --in.";
                return false;
            }
            if (options.StdOut)
            {
                error = "--interactive cannot be used with --stdout.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            {
                error = "--interactive cannot be used with --save-cast.";
                return false;
            }
            if (
                !string.IsNullOrWhiteSpace(options.ReplayPath)
                || !string.IsNullOrWhiteSpace(options.ReplaySavePath)
            )
            {
                error = "--interactive cannot be used with replay options.";
                return false;
            }
        }

        if (
            (options.EmbedCast || options.EmbedLogs || options.EmbedReplay)
            && !options.StdOut
            && !string.IsNullOrEmpty(Path.GetExtension(options.OutputPath))
            && !string.Equals(
                Path.GetExtension(options.OutputPath),
                ".svg",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            error = "Embed options require SVG output.";
            return false;
        }
        if (options.EmbedReplay && string.IsNullOrWhiteSpace(options.Command))
        {
            error = "--embed-replay requires a command to be specified.";
            return false;
        }
        if (options.EmbedReplay && !string.IsNullOrWhiteSpace(options.ReplayPath))
        {
            error = "--embed-replay and --replay cannot be used together.";
            return false;
        }
        if (
            !string.IsNullOrWhiteSpace(options.Command)
            && (!string.IsNullOrWhiteSpace(options.InputCastPath))
        )
        {
            error = "--command and --in cannot be used together.";
            return false;
        }
        if (
            !string.IsNullOrWhiteSpace(options.ReplayPath)
            && !string.IsNullOrWhiteSpace(options.ReplaySavePath)
        )
        {
            error = "--replay and --replay-save cannot be used together.";
            return false;
        }
        if (
            !string.IsNullOrWhiteSpace(options.ReplayPath)
            && string.IsNullOrWhiteSpace(options.Command)
            && options.Workflow != Workflow.Replay
        )
        {
            error = "--replay requires a command to be specified.";
            return false;
        }
        if (
            !string.IsNullOrWhiteSpace(options.ReplaySavePath)
            && string.IsNullOrWhiteSpace(options.Command)
        )
        {
            error = "--replay-save requires a command to be specified.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> NormalizeCompatibilitySyntax(IReadOnlyList<string> args)
    {
        var normalized = new List<string>(args.Count + 2);
        if (UsesLegacyRootInvocation(args))
        {
            if (args.Contains("-i") || args.Contains("--interactive"))
            {
                normalized.Add("interactive");
            }
            else
            {
                normalized.Add("capture");
                normalized.Add("--legacy-root");
            }
        }

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (token.StartsWith("--verbose=", StringComparison.Ordinal))
            {
                normalized.Add("--verbose");
                var path = token["--verbose=".Length..];
                if (!string.IsNullOrWhiteSpace(path))
                {
                    normalized.Add("--verbose-log");
                    normalized.Add(path);
                }
                continue;
            }

            if (token == "--verbose")
            {
                normalized.Add(token);
                if (index + 1 < args.Count && IsVerboseLogPathValue(args[index + 1]))
                {
                    normalized.Add("--verbose-log");
                    normalized.Add(args[++index]);
                }
                else if (
                    index + 1 < args.Count
                    && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                )
                {
                    normalized.Add("--");
                }
                continue;
            }

            if (!TryGetBackgroundValue(token, out var inlineValue))
            {
                normalized.Add(token);
                continue;
            }

            normalized.Add("--background");
            if (inlineValue is not null)
            {
                normalized.Add(inlineValue);
            }
            else if (index + 1 < args.Count)
            {
                normalized.Add(args[++index]);
            }
            else
            {
                continue;
            }

            if (
                index + 1 < args.Count
                && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                && args[index + 1] != "--"
                && LooksLikeBackgroundValue(args[index + 1])
            )
            {
                normalized.Add("--background");
                normalized.Add(args[++index]);
            }
        }
        return normalized;
    }

    private static bool UsesLegacyRootInvocation(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "--help" or "--version" or "-?" or "/?")
            return false;

        return args[0]
            is not (
                "capture"
                or "interactive"
                or "replay"
                or "cast"
                or "theme"
                or "status"
                or "live-server"
                or "tmux"
                or "completions"
            );
    }

    private static bool TryGetBackgroundValue(string token, out string? inlineValue)
    {
        if (token == "--background")
        {
            inlineValue = null;
            return true;
        }
        if (token.StartsWith("--background=", StringComparison.Ordinal))
        {
            inlineValue = token["--background=".Length..];
            return true;
        }

        inlineValue = null;
        return false;
    }

    private static bool IsVerboseLogPathValue(string token) =>
        !token.StartsWith("-", StringComparison.Ordinal)
        && token != "--"
        && (
            token.Contains('/', StringComparison.Ordinal)
            || token.Contains('\\', StringComparison.Ordinal)
            || token.StartsWith(".", StringComparison.Ordinal)
            || token.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        );

    private static bool LooksLikeBackgroundValue(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (token.StartsWith("#", StringComparison.Ordinal))
            return true;
        if (token.Contains('(') && token.TrimEnd().EndsWith(")", StringComparison.Ordinal))
            return true;
        if (
            token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
            return true;

        var lower = token.ToLowerInvariant();
        return lower.EndsWith(".png", StringComparison.Ordinal)
            || lower.EndsWith(".jpg", StringComparison.Ordinal)
            || lower.EndsWith(".jpeg", StringComparison.Ordinal)
            || lower.EndsWith(".gif", StringComparison.Ordinal)
            || lower.EndsWith(".svg", StringComparison.Ordinal)
            || lower.EndsWith(".webp", StringComparison.Ordinal)
            || lower.EndsWith(".bmp", StringComparison.Ordinal);
    }
}
