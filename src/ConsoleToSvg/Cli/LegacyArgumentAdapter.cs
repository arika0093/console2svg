using System;
using System.Collections.Generic;

namespace ConsoleToSvg.Cli;

/// <summary>
/// Normalizes the historical verb-less syntax into a canonical CAF command line.
/// It does not parse or bind option values.
/// </summary>
internal static class LegacyArgumentAdapter
{
    public static string[] Normalize(string[] args)
    {
        var command = "capture";
        string? sourceArgument = null;
        string? positionalCommand = null;
        var normalized = new List<string>(args.Length + 3);
        string[]? escaped = null;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (token == "--")
            {
                escaped = args[(index + 1)..];
                break;
            }

            if (token is "-i" or "--interactive")
            {
                command = "shell";
                continue;
            }

            if (token == "--in" && index + 1 < args.Length)
            {
                command = "render";
                sourceArgument = args[++index];
                continue;
            }
            if (token.StartsWith("--in=", StringComparison.Ordinal))
            {
                command = "render";
                sourceArgument = token["--in=".Length..];
                continue;
            }

            if (token == "--replay" && index + 1 < args.Length)
            {
                command = "replay";
                sourceArgument = args[++index];
                continue;
            }
            if (token.StartsWith("--replay=", StringComparison.Ordinal))
            {
                command = "replay";
                sourceArgument = token["--replay=".Length..];
                continue;
            }

            if (token == "-h" && index + 1 < args.Length)
            {
                normalized.Add("--height");
                normalized.Add(args[++index]);
                continue;
            }

            if (token is "-d" or "--window")
            {
                normalized.Add(token);
                if (index + 1 < args.Length && IsWindowValue(args[index + 1]))
                {
                    normalized.Add(args[++index]);
                }
                else
                {
                    normalized.Add("macos");
                }
                continue;
            }

            if (token == "--verbose")
            {
                normalized.Add(token);
                if (index + 1 < args.Length && IsLogPath(args[index + 1]))
                {
                    normalized.Add("--verbose-log");
                    normalized.Add(args[++index]);
                }
                continue;
            }
            if (token.StartsWith("--verbose=", StringComparison.Ordinal))
            {
                normalized.Add("--verbose");
                normalized.Add("--verbose-log");
                normalized.Add(token["--verbose=".Length..]);
                continue;
            }

            if (token == "--background" && index + 1 < args.Length)
            {
                var first = args[++index];
                if (index + 1 < args.Length && LooksLikeBackground(args[index + 1]))
                {
                    normalized.Add(token);
                    normalized.Add($"{first},{args[++index]}");
                }
                else
                {
                    normalized.Add(token);
                    normalized.Add(first);
                }
                continue;
            }

            if (token == "--mask" && index + 1 < args.Length)
            {
                var masks = new List<string>();
                while (
                    index + 1 < args.Length
                    && args[index + 1] != "--"
                    && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                )
                {
                    masks.Add(args[++index]);
                }
                normalized.Add(token);
                normalized.Add(string.Join(',', masks));
                continue;
            }

            var equalsIndex = token.IndexOf('=');
            if (equalsIndex > 0)
            {
                var name = token[..equalsIndex];
                var value = token[(equalsIndex + 1)..];
                normalized.Add(name);
                if (
                    RequiresValue(name)
                    || name is "--background" or "--mask" or "-d" or "--window"
                )
                {
                    normalized.Add(value);
                }
                continue;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                if (positionalCommand is null)
                {
                    positionalCommand = token;
                }
                else
                {
                    normalized.Add(token);
                }
                continue;
            }

            normalized.Add(token);
            if (
                !token.Contains('=', StringComparison.Ordinal)
                && RequiresValue(token)
                && index + 1 < args.Length
            )
            {
                normalized.Add(args[++index]);
            }
        }

        var result = new List<string>(normalized.Count + 4) { command };
        if (sourceArgument is not null)
        {
            result.Add(sourceArgument);
        }
        result.AddRange(normalized);

        if (escaped is not null)
        {
            result.Add("--");
            result.AddRange(escaped);
        }
        else if (positionalCommand is not null)
        {
            result.Add("--");
            result.Add(positionalCommand);
        }

        return result.ToArray();
    }

    private static bool RequiresValue(string option)
    {
        var name = option.Split('=', 2)[0];
        return name
            is "--format"
                or "-o"
                or "--out"
                or "-m"
                or "--mode"
                or "-w"
                or "--width"
                or "--height"
                or "--frame"
                or "--time"
                or "--crop-top"
                or "--crop-right"
                or "--crop-bottom"
                or "--crop-left"
                or "--theme"
                or "--forecolor"
                or "--backcolor"
                or "--font"
                or "--fontsize"
                or "--font-size"
                or "--padding"
                or "--fps"
                or "--sleep"
                or "--fadeout"
                or "--timing"
                or "--coalesce-ms"
                or "--opacity"
                or "--adjust"
                or "--timeout"
                or "--save-cast"
                or "--replay-save"
                or "--header"
                or "--prompt"
                or "--pc-padding"
                or "--save-frames"
                or "--size"
                or "--svg-converter"
                or "--verbose-log";
    }

    private static bool IsLogPath(string value) =>
        !value.StartsWith("-", StringComparison.Ordinal)
        && (
            value.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
        );

    private static bool IsWindowValue(string value) =>
        !value.StartsWith("-", StringComparison.Ordinal)
        && (
            value is "none" or "macos" or "windows" or "transparent"
            || value.EndsWith("-pc", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        );

    private static bool LooksLikeBackground(string value) =>
        value.StartsWith("#", StringComparison.Ordinal)
        || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)
        || value.Contains('/', StringComparison.Ordinal)
        || value.Contains('\\', StringComparison.Ordinal)
        || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
}
