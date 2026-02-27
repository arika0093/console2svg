using System;
using System.Globalization;

namespace ConsoleToSvg.Cli;

public static class OptionParser
{
    public static string ShortHelpText =>
        """
            console2svg - Convert terminal output to SVG

            Usage:
                my-command | console2svg [options]
                console2svg "my-command with-args" [options]
                console2svg [options] -- my-command with args

            Major options:
                -c, --with-command        Prepend the command line to the output.
                -o, --out <path>          Output SVG path (default: output.svg).
                -w, --width <int>         Terminal width in characters (default: auto).
                -h, --height <int>        Terminal height in rows (default: auto).
                -v                        Output animated SVG (alias for --mode video).
                -d, --window [style]      Window chrome: none, macos, windows, macos-pc, windows-pc.
                --opacity <0-1>           Opacity of window/terminal content (default: 1).
                --background <color> [color]  Background color, gradient, or image path.
                --crop-top/bottom/left/right  Crop by px, ch, or text pattern.
                --verbose                 Enable verbose logging.

            For full option list, see --help.
            """;

    public static string HelpText =>
        """
            console2svg - Convert terminal output to SVG

            Usage:
                my-command | console2svg [options]
                console2svg "my-command with-args" [options]
                console2svg [options] -- my-command with args

            Options (Common):
                -c, --with-command        Prepend the command line to the output as if typed in a terminal.
                -o, --out <path>          Output SVG path (default: output.svg).
                -m, --mode <image|video>  Output mode (default: image).
                -v                        is alias for --mode video.
                -w, --width <int>         Terminal width in characters (default: auto).
                -h, --height <int>        Terminal height in rows (default: auto).
                --font <family>           CSS font-family for SVG text.
                --in <path>               Read existing asciicast file.
                --save-cast <path>        Save captured output as asciicast file.
                --help                    Show help.
                --verbose                 Enable verbose logging.
                --version                 Show version and exit.

            Options (Appearance):
                -d, --window [none|macos|windows|macos-pc|windows-pc]
                                          Terminal window chrome style (default: none, or macos if specified without a value).
                --opacity <0-1>           Background fill opacity (default: 1).
                --theme <dark|light>      Color theme (default: dark).
                --padding <px>            Outer padding in pixels (default: 2, or 8 when window is set).
                --background <color|path> [color]
                    Desktop background. Accepts:
                        Solid color  : --background "#rrggbb"
                        Gradient     : --background "#from" "#to"
                                       --background "#from:#to"
                                       --background "#from" --background "#to"
                        Image        : --background path/to/image.png
                    Colors: #hex, rgb(), hsl(), oklch(), named colors.

            Options (Image mode):
                --frame <int>             Frame index for image mode.
                --crop-top <value>        Crop top by px, ch, or text pattern (e.g. 10px, 2ch, sometext, summary:-3).
                --crop-bottom <value>     Crop bottom by px, ch, or text pattern.
                --crop-right <value>      Crop right by px or ch.
                --crop-left <value>       Crop left by px or ch.

            Options (Video mode):
                --no-loop                 Disable loop for animated SVG playback in video mode (default: loop).
                --fps <value>             Max FPS for animated SVG frame sampling (default: 12).
                --sleep <sec>             Wait time after execution completes in video mode (default: 2).
                --fadeout <sec>           Fade-out duration at end of video (default: 0).
            """;

    public static bool TryParse(
        string[] args,
        out AppOptions? options,
        out string? error,
        out bool showHelp
    )
    {
        options = new AppOptions();
        error = null;
        showHelp = false;

        var i = 0;
        while (i < args.Length)
        {
            var token = args[i];

            if (string.Equals(token, "--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    error = "Expected command after --.";
                    return false;
                }

                if (options.Command != null)
                {
                    error =
                        "Command is already specified. Use either --command/positional argument or -- delimiter, not both.";
                    return false;
                }

                options.Command = string.Join(' ', args, i + 1, args.Length - (i + 1));
                break;
            }

            if (string.Equals(token, "--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                return true;
            }

            // Treat bare positional arguments as the command
            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                if (options.Command != null)
                {
                    error =
                        "Multiple positional arguments are not allowed. Use --command to specify the command.";
                    return false;
                }

                options.Command = token;
                i++;
                continue;
            }

            if (!TrySplitToken(token, out var name, out var inlineValue))
            {
                error = $"Invalid argument: {token}";
                return false;
            }

            var value = inlineValue;
            if (value is null && RequiresValue(name))
            {
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for option: {name}";
                    return false;
                }

                i++;
                value = args[i];
            }
            else if (
                value is null
                && (
                    string.Equals(name, "-d", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "--window", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                // Value is optional: consume next token only if it is a known window style
                if (i + 1 < args.Length && IsWindowStyleValue(args[i + 1]))
                {
                    i++;
                    value = args[i];
                }
                // else: value stays null → ApplyOption defaults to "macos"
            }

            if (!ApplyOption(options, name, value, out error))
            {
                return false;
            }

            // --background: optionally consume the very next token as the second (end) color
            // when it looks like a color/path value and not another flag or command.
            if (
                (string.Equals(name, "--background", StringComparison.OrdinalIgnoreCase))
                && options.Background.Count == 1
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                && !string.Equals(args[i + 1], "--", StringComparison.Ordinal)
                && LooksLikeBackgroundValue(args[i + 1])
            )
            {
                i++;
                options.Background.Add(args[i]);
            }

            i++;
        }

        if (!Validate(options, out error))
        {
            return false;
        }

        return true;
    }

    private static bool TrySplitToken(string token, out string name, out string? inlineValue)
    {
        var index = token.IndexOf('=');
        if (index < 0)
        {
            name = token;
            inlineValue = null;
            return true;
        }

        name = token.Substring(0, index);
        inlineValue = token.Substring(index + 1);
        return true;
    }

    private static bool RequiresValue(string name)
    {
        return !string.Equals(name, "--help", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--version", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--no-loop", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "-c", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--with-command", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "-v", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--verbose", StringComparison.OrdinalIgnoreCase)
            // -d/--window is optional-value; handled separately in the main loop
            && !string.Equals(name, "-d", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--window", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowStyleValue(string token) =>
        string.Equals(token, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "macos", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "windows", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "macos-pc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "windows-pc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a token looks like a color value or image path that can
    /// be used as a --background argument (as opposed to a positional command name).
    /// </summary>
    private static bool LooksLikeBackgroundValue(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        // CSS hex colors
        if (token.StartsWith("#", StringComparison.Ordinal))
            return true;
        // CSS function colors: rgb(), rgba(), hsl(), hsla(), oklch(), color(), ...
        if (token.Contains('(') && token.TrimEnd().EndsWith(")", StringComparison.Ordinal))
            return true;
        // URLs
        if (
            token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
            return true;
        // File paths with known image extensions
        var lower = token.ToLowerInvariant();
        return lower.EndsWith(".png", StringComparison.Ordinal)
            || lower.EndsWith(".jpg", StringComparison.Ordinal)
            || lower.EndsWith(".jpeg", StringComparison.Ordinal)
            || lower.EndsWith(".gif", StringComparison.Ordinal)
            || lower.EndsWith(".svg", StringComparison.Ordinal)
            || lower.EndsWith(".webp", StringComparison.Ordinal)
            || lower.EndsWith(".bmp", StringComparison.Ordinal);
    }

    private static bool ApplyOption(
        AppOptions options,
        string name,
        string? value,
        out string? error
    )
    {
        error = null;
        switch (name)
        {
            case "-v":
                options.Mode = OutputMode.Video;
                return true;
            case "--verbose":
                options.Verbose = true;
                return true;
            case "--version":
                options.ShowVersion = true;
                return true;
            case "-c":
            case "--with-command":
                options.WithCommand = true;
                return true;
            case "--in":
                options.InputCastPath = value;
                return true;
            case "-o":
            case "--out":
                options.OutputPath = value ?? options.OutputPath;
                return true;
            case "-m":
            case "--mode":
                if (string.Equals(value, "image", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = OutputMode.Image;
                    return true;
                }

                if (string.Equals(value, "video", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = OutputMode.Video;
                    return true;
                }

                error = "--mode must be image or video.";
                return false;
            case "-w":
            case "--width":
                if (!TryParseInt(value, "--width", out var width, out error))
                {
                    return false;
                }

                options.Width = width;
                return true;
            case "-h":
            case "--height":
                if (!TryParseInt(value, "--height", out var height, out error))
                {
                    return false;
                }

                options.Height = height;
                return true;
            case "--frame":
                if (!TryParseInt(value, "--frame", out var frame, out error))
                {
                    return false;
                }

                options.Frame = frame;
                return true;
            case "--crop-top":
                options.CropTop = value ?? "0";
                return true;
            case "--crop-right":
                options.CropRight = value ?? "0";
                return true;
            case "--crop-bottom":
                options.CropBottom = value ?? "0";
                return true;
            case "--crop-left":
                options.CropLeft = value ?? "0";
                return true;
            case "--theme":
                options.Theme = string.IsNullOrWhiteSpace(value) ? "dark" : value;
                return true;
            case "-d":
            case "--window":
                if (string.IsNullOrWhiteSpace(value))
                {
                    // No value supplied: default to macos
                    options.Window = "macos";
                    return true;
                }

                if (
                    !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "macos", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "macos-pc", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "windows-pc", StringComparison.OrdinalIgnoreCase)
                )
                {
                    error = "--window must be none, macos, windows, macos-pc, or windows-pc.";
                    return false;
                }

                options.Window = value;
                return true;
            case "--padding":
                if (!TryParseDouble(value, "--padding", out var padding, out error))
                {
                    return false;
                }

                options.Padding = padding;
                return true;
            case "--no-loop":
                options.Loop = false;
                return true;
            case "--fps":
                if (!TryParseDouble(value, "--fps", out var fps, out error))
                {
                    return false;
                }

                options.VideoFps = fps;
                return true;
            case "--sleep":
                if (!TryParseDouble(value, "--sleep", out var sleep, out error))
                {
                    return false;
                }

                options.VideoSleep = sleep;
                return true;
            case "--fadeout":
                if (!TryParseDouble(value, "--fadeout", out var fadeout, out error))
                {
                    return false;
                }

                options.VideoFadeOut = fadeout;
                return true;
            case "--opacity":
                if (!TryParseDouble(value, "--opacity", out var opacity, out error))
                {
                    return false;
                }

                options.Opacity = opacity;
                return true;
            case "--background":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--background requires a value.";
                    return false;
                }

                if (options.Background.Count >= 2)
                {
                    error =
                        "--background can be specified at most twice (start color and end color).";
                    return false;
                }

                // Support "#from:#to" colon-separated gradient shorthand.
                // Skip split when the value is a URL (contains "://").
                if (
                    options.Background.Count == 0
                    && value.Contains(':', StringComparison.Ordinal)
                    && !value.Contains("://", StringComparison.Ordinal)
                )
                {
                    var colonIdx = value.IndexOf(':', StringComparison.Ordinal);
                    var part1 = value.Substring(0, colonIdx);
                    var part2 = value.Substring(colonIdx + 1);
                    if (!string.IsNullOrWhiteSpace(part1) && !string.IsNullOrWhiteSpace(part2))
                    {
                        options.Background.Add(part1);
                        options.Background.Add(part2);
                        return true;
                    }
                }

                options.Background.Add(value);
                return true;
            case "--font":
                options.Font = value;
                return true;
            case "--save-cast":
                options.SaveCastPath = value;
                return true;
            default:
                error = $"Unknown option: {name}";
                return false;
        }
    }

    private static bool TryParseInt(
        string? value,
        string option,
        out int parsedValue,
        out string? error
    )
    {
        error = null;
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing value for {option}.";
            return false;
        }

        if (
            !int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsedValue
            )
        )
        {
            error = $"{option} must be integer.";
            return false;
        }

        return true;
    }

    private static bool TryParseDouble(
        string? value,
        string option,
        out double parsedValue,
        out string? error
    )
    {
        error = null;
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing value for {option}.";
            return false;
        }

        if (
            !double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsedValue
            )
        )
        {
            error = $"{option} must be a number.";
            return false;
        }

        return true;
    }

    private static bool Validate(AppOptions options, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            error = "--out must not be empty.";
            return false;
        }

        if (options.Width.HasValue && options.Width.Value <= 0)
        {
            error = "--width must be greater than 0.";
            return false;
        }

        if (options.Height.HasValue && options.Height.Value <= 0)
        {
            error = "--height must be greater than 0.";
            return false;
        }

        if (options.Frame is < 0)
        {
            error = "--frame must be non-negative.";
            return false;
        }

        if (
            options.Padding.HasValue
            && (
                double.IsNaN(options.Padding.Value)
                || double.IsInfinity(options.Padding.Value)
                || options.Padding.Value < 0
            )
        )
        {
            error = "--padding must be a non-negative finite number.";
            return false;
        }

        if (
            double.IsNaN(options.VideoFps)
            || double.IsInfinity(options.VideoFps)
            || options.VideoFps <= 0
        )
        {
            error = "--fps must be greater than 0.";
            return false;
        }

        if (
            double.IsNaN(options.VideoSleep)
            || double.IsInfinity(options.VideoSleep)
            || options.VideoSleep < 0
        )
        {
            error = "--sleep must be a non-negative number.";
            return false;
        }

        if (
            double.IsNaN(options.VideoFadeOut)
            || double.IsInfinity(options.VideoFadeOut)
            || options.VideoFadeOut < 0
        )
        {
            error = "--fadeout must be a non-negative number.";
            return false;
        }

        if (
            double.IsNaN(options.Opacity)
            || double.IsInfinity(options.Opacity)
            || options.Opacity < 0
            || options.Opacity > 1
        )
        {
            error = "--opacity must be a number between 0 and 1.";
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(options.Command)
            && !string.IsNullOrWhiteSpace(options.InputCastPath)
        )
        {
            error = "--command and --in cannot be used together.";
            return false;
        }

        return true;
    }
}
