using System;
using System.Globalization;

namespace ConsoleToSvg.Cli;

public static class OptionParser
{
    public static string HelpText =>
        """
            console2svg - Convert terminal output to SVG

            Usage:
              my-command | console2svg [options]
              console2svg "my-command with-args" [options]
              console2svg [options] -- my-command with args

            Options:
              -c, --command <value>          Execute command in PTY mode.
              -o, --out <path>               Output SVG path (default: output.svg).
              -m, --mode <image|video>       Output mode (default: image).
              -w, --width <int>              Terminal width in characters (default: auto).
              -h, --height <int>             Terminal height in rows (default: auto).
              -v, --verbose                  Enable verbose logging.
              --version                      Show version and exit.
              --frame <int>                  Frame index for image mode.
              --crop-top <value>             Crop top by px, ch, or text pattern (examples: 10px, 2ch, ---, summary:-3).
              --crop-right <value>           Crop right by px or ch.
              --crop-bottom <value>          Crop bottom by px, ch, or text pattern (examples: 10px, 2ch, ---, summary:-3).
              --crop-left <value>            Crop left by px or ch.
              --theme <dark|light>           Color theme (default: dark).
              --window <none|macos|windows>  Terminal window chrome style (default: none).
              --padding <px>                 Outer padding in pixels around terminal content (default: 2).
              --loop                         Loop animated SVG playback in video mode (default: false).
              --fps <value>                  Max FPS for animated SVG frame sampling (default: 12).
              --font <family>                CSS font-family for SVG text (default: system monospace).
              --in <path>                    Read existing asciicast file.
              --save-cast <path>             Save captured output as asciicast file.
              --help                         Show help.
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

            if (!ApplyOption(options, name, value, out error))
            {
                return false;
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
            && !string.Equals(name, "--loop", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "-v", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--verbose", StringComparison.OrdinalIgnoreCase);
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
            case "--verbose":
                options.Verbose = true;
                return true;
            case "--version":
                options.ShowVersion = true;
                return true;
            case "-c":
            case "--command":
                options.Command = value;
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
            case "--window":
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = "--window must be none, macos, or windows.";
                    return false;
                }

                if (
                    !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "macos", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase)
                )
                {
                    error = "--window must be none, macos, or windows.";
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
            case "--loop":
                options.Loop = true;
                return true;
            case "--fps":
                if (!TryParseDouble(value, "--fps", out var fps, out error))
                {
                    return false;
                }

                options.VideoFps = fps;
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
            double.IsNaN(options.Padding)
            || double.IsInfinity(options.Padding)
            || options.Padding < 0
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
