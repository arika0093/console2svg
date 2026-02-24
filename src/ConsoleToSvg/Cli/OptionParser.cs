using System;
using System.Globalization;

namespace ConsoleToSvg.Cli;

public static class OptionParser
{
    public static string HelpText =>
        """
        console2svg - Convert terminal output to SVG

        Usage:
          my-command --color=always | console2svg [options]
          console2svg --command "git log --oneline" [options]
          console2svg --in session.cast [options]

        Options:
          -c, --command <value>      Execute command in PTY mode.
          --in <path>                Read existing asciicast file.
          -o, --out <path>           Output SVG path (default: output.svg).
          --mode <image|video>       Output mode (default: image).
          --width <int>              Terminal width in characters (default: 80).
          --height <int>             Terminal height in rows (default: 24).
          --frame <int>              Frame index for image mode.
          --crop-top <value>         Crop top by px or ch (example: 10px, 2ch).
          --crop-right <value>       Crop right by px or ch.
          --crop-bottom <value>      Crop bottom by px or ch.
          --crop-left <value>        Crop left by px or ch.
          --theme <dark|light>       Color theme (default: dark).
          --save-cast <path>         Save captured output as asciicast file.
          -h, --help                 Show help.
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

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (string.Equals(token, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                return true;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Unexpected argument: {token}";
                return false;
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
        return !string.Equals(name, "-h", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApplyOption(AppOptions options, string name, string? value, out string? error)
    {
        error = null;
        switch (name)
        {
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
            case "--width":
                if (!TryParseInt(value, "--width", out var width, out error))
                {
                    return false;
                }

                options.Width = width;
                return true;
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
            case "--save-cast":
                options.SaveCastPath = value;
                return true;
            default:
                error = $"Unknown option: {name}";
                return false;
        }
    }

    private static bool TryParseInt(string? value, string option, out int parsedValue, out string? error)
    {
        error = null;
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Missing value for {option}.";
            return false;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
        {
            error = $"{option} must be integer.";
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

        if (options.Width <= 0)
        {
            error = "--width must be greater than 0.";
            return false;
        }

        if (options.Height <= 0)
        {
            error = "--height must be greater than 0.";
            return false;
        }

        if (options.Frame is < 0)
        {
            error = "--frame must be non-negative.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.Command) && !string.IsNullOrWhiteSpace(options.InputCastPath))
        {
            error = "--command and --in cannot be used together.";
            return false;
        }

        return true;
    }
}