using System;
using System.Globalization;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public static partial class OptionParser
{
    public static string ShortHelpText =>
        $"""
            console2svg - Convert terminal output to SVG [Ver: {ThisAssembly.AssemblyInformationalVersion}]

            Usage:
                my-command | console2svg [options]
                console2svg "my-command with-args" [options]
                console2svg [options] -- my-command with args

            Major options:
                -o, --out <path>          Output file path (default: output.svg).
                                          Non-SVG extensions trigger ffmpeg conversion (e.g. output.png, output.mp4).
                -w, --width <int|adjust>  Terminal width in characters (default: adjust).
                                          Specify an integer to use a fixed width.
                -h, --height <int|adjust> Terminal height in rows (default: adjust).
                                          Specify an integer to use a fixed height.
                -v, --video               Output animated SVG (alias for --mode video).
                -i, --interactive         Run an interactive shell; F9 records, F12 pauses, and F10 takes a screenshot.
                                          Use -- to start another interactive program (e.g. -i -- pwsh).
                -c, --with-command        Prepend the command line to the output.
                -d, --window [style]      Window chrome: none, macos, windows, macos-pc, windows-pc, transparent.
                --background <color> [color]  Background color, gradient, or image path.
                --crop-top/bottom/left/right  Crop by px, ch, or text pattern.
                --header <text>           Override command header text.
                --prompt <text>           Override prompt prefix for -c (default: $ or # when root).
                --verbose [path]          Enable verbose logging (log to path, default: console2svg.log).

            For full option list, see --help.
            """;

    public static string HelpText =>
        $"""
            console2svg - Convert terminal output to SVG [Ver: {ThisAssembly.AssemblyInformationalVersion}]

            Usage:
                my-command | console2svg [options]
                console2svg "my-command with-args" [options]
                console2svg [options] -- my-command with args

            Options (Common):
                -o, --out <path>          Output file path (default: output.svg).
                                          Extension determines format:
                                            .svg          - SVG output (default, no external tools required).
                                            .png          - Raster image via resvg.
                                            .mp4/.webm/…  - Video using frame sequences via ffmpeg.
                --stdout                  Write SVG to stdout instead of a file.
                                          PTY output forwarding is suppressed so the pipe receives only SVG.
                -m, --mode <image|video|repeat>  Output mode (default: image).
                -v, --video               Output animated SVG (alias for --mode video).
                -w, --width <int|adjust>  Terminal width in characters (default: adjust).
                                          Uses the current terminal width. Specify an integer for a fixed width.
                -h, --height <int|adjust> Terminal height in rows (default: adjust).
                                          Uses the current terminal height. Specify an integer for a fixed height.
                --in <path>               Read existing asciicast file.
                --save-cast <path>        Save captured output as asciicast file.
                --verbose [path]          Enable verbose logging; write to path (default: console2svg.log).
                --help                    Show help.
                --version                 Show version and exit.
                --timeout <sec>           Stop recording after specified seconds (e.g. 5, 0.5).

            Options (Appearance):
                -c, --with-command        Prepend the command line to the output as if typed in a terminal.
                --header <text>           Override command header text (shown even without -c).
                --prompt <text>           Prompt prefix for -c (default: $ or # when root).
                -d, --window [none|macos|windows|macos-pc|windows-pc|transparent|path/to/chrome.json]
                    Terminal window chrome style (default: none, or macos if specified without a value).
                    Built-in styles: none, macos, windows, transparent.
                    Any built-in style can be suffixed with -pc to enable desktop (floating window) mode.
                    Custom: provide a path to a .json chrome definition file.
                --pcmode                  Enable PC (desktop) mode for the selected window style.
                                          Appends -pc to any window style that does not already end in -pc.
                --pc-padding <px>         Override the outer desktop padding in PC mode (default: 20).
                --opacity <0-1>           Background fill opacity (default: 1).
                --theme <dark|light>      Color theme (default: dark).
                --forecolor <color>       Override default foreground color.
                --backcolor <color>       Override the terminal's own background color.
                                          Unlike --background, this affects the terminal interior rather than the outer canvas.
                --padding <px>            Outer padding in pixels (default: 8).
                --adjust <value>          SVG text lengthAdjust (default: spacing).
                --background <color|path> [color] Desktop background. Accepts:
                    Solid color  : --background "#rrggbb"
                    Gradient     : --background "#from" "#to"
                                   --background "#from:#to"
                                   --background "#from" --background "#to"
                    Image        : --background path/to/image.png
                --font <family>           CSS font-family for SVG text.
                --fontsize <px>           Font size in pixels (default: 14).
                --mask <text> [text...]   Mask sensitive strings in the output by replacing them with ***.
                                          Useful for hiding PC names, directories, credentials, etc.
                                          Multiple patterns can be specified: --mask "MyPC" "secret-dir"

            Options (Image mode):
                --frame <int>             Frame index for image mode.
                --time <sec>              Time in seconds for image mode (e.g. --time 3.5).
                                          Alternatively, a range: --time 1.5-3.0 (works with --save-frames and --video).
                                          Mutually exclusive with --frame.
                --size <WxH>              Output image pixel dimensions. Formats:
                                            800         - width only (height scaled proportionally).
                                            800x*       - width only (height scaled proportionally).
                                            *x600       - height only (width scaled proportionally).
                                            800x600     - both; content is centered, background extended.
                --save-frames <dir>       Save each visual frame as a separate static SVG in the given directory.
                --crop-top <value>        Crop top by px, ch, or text pattern (e.g. 10px, 2ch, sometext, summary:-3).
                --crop-bottom <value>     Crop bottom by px, ch, or text pattern.
                --crop-right <value>      Crop right by px or ch.
                --crop-left <value>       Crop left by px or ch.

            Options (Video/Repeat mode):
                --no-loop                 Disable loop for animated SVG playback in video mode (default: loop).
                --fps <value>             Max FPS for animated SVG frame sampling (default: 12).
                --sleep <sec>             Wait time after execution completes in video mode (default: 0).
                --fadeout <sec>           Fade-out duration at end of video (default: 0).
                --coalesce-ms <ms|auto>   Coalesce output chunks within the given gap (default: auto; 0 disables).

            Options (Interactive mode):
                -i, --interactive         Run an interactive shell. F9 starts/stops an animation recording;
                                          F12 pauses/resumes it, and F10 saves a static screenshot. These keys work independently of -v.
                                          Use -- to start another interactive program, preserving its arguments
                                          (e.g. -i -- pwsh, -i -- vim README.md).

            Options (Advanced):
                --no-colorenv             Disable PTY color environment overrides (TERM/COLORTERM/FORCE_COLOR).
                --no-delete-envs          Keep CI/TF_BUILD in shell execution environment.
                --timing <deterministic|realtime>
                                          Video timing mode (default: deterministic).
                                          deterministic: normalize frame times to reduce output diffs.
                                          realtime: preserve measured event timing as-is.
                --svg-converter <auto|ffmpeg|rsvg-convert|resvg>
                    SVG → raster converter (default: auto).
                    auto: prefer the bundled resvg host, then ffmpeg+librsvg.
                    ffmpeg: force ffmpeg (requires librsvg input device).
                    rsvg-convert: force the rsvg-convert CLI tool (librsvg2-bin / brew install librsvg).
                    resvg: force the bundled resvg renderer.
                    When a fallback handles SVG→PNG, ffmpeg is only used for subsequent format conversion.
            """;

    // ANSI color codes
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Dim = "\x1b[2m";
    private const string Cyan = "\x1b[36m";
    private const string Yellow = "\x1b[33m";
    private const string Green = "\x1b[32m";
    private const string Red = "\x1b[31m";
    private const string Magenta = "\x1b[35m";

    public static string ColorizeHelp(string text)
    {
        var lines = text.Split('\n');
        var result = new System.Text.StringBuilder(text.Length + 512);

        // Detect the standard option-line indentation by looking at the first
        // line that starts with spaces then a dash.
        var optionIndent = DetectOptionIndent(lines);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (i > 0)
            {
                result.Append('\n');
            }

            // Title line (first line with version)
            if (i == 0 && line.StartsWith("console2svg -", StringComparison.Ordinal))
            {
                result.Append(Bold).Append(Green).Append(line).Append(Reset);
                continue;
            }

            // Section headers
            if (line.StartsWith("Usage:", StringComparison.Ordinal)
                || line.StartsWith("Major options:", StringComparison.Ordinal)
                || line.StartsWith("Options (", StringComparison.Ordinal)
                || line.StartsWith("For full option list", StringComparison.Ordinal))
            {
                result.Append(Bold).Append(Yellow).Append(line).Append(Reset);
                continue;
            }

            // Option lines: must have the standard option indentation and start with -
            var leadingSpaces = CountLeadingSpaces(line);
            var trimmed = line.TrimStart();
            if (leadingSpaces == optionIndent && trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                result.Append(ColorizeOptionLine(line));
                continue;
            }

            // Continuation lines (indented descriptions) - colorize quoted strings and parens
            if (leadingSpaces > optionIndent && !string.IsNullOrWhiteSpace(trimmed))
            {
                result.Append(ColorizeDescription(line));
                continue;
            }

            // Default line
            result.Append(line);
        }

        return result.ToString();
    }

    private static int DetectOptionIndent(string[] lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var spaces = CountLeadingSpaces(line);
            if (spaces > 0 && spaces < line.Length && line[spaces] == '-')
            {
                return spaces;
            }
        }

        return 4; // fallback
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }
        return count;
    }

    private static string ColorizeOptionLine(string line)
    {
        var result = new System.Text.StringBuilder(line.Length + 64);

        // Find where description starts.
        // First try: double space after the option part.
        var descStart = -1;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] == ' ' && line[i + 1] == ' ')
            {
                var before = line[..i].Trim();
                if (before.StartsWith("-", StringComparison.Ordinal))
                {
                    descStart = i;
                    break;
                }
            }
        }

        // Fallback: find the first space after the last '>' or ']' argument placeholder.
        if (descStart < 0)
        {
            var lastClose = -1;
            for (var i = line.Length - 1; i >= 0; i--)
            {
                if (line[i] == '>' || line[i] == ']')
                {
                    lastClose = i;
                    break;
                }
            }

            if (lastClose >= 0 && lastClose + 1 < line.Length)
            {
                descStart = lastClose + 1;
            }
        }

        if (descStart > 0)
        {
            result.Append(ColorizeOptionPart(line[..descStart]));
            result.Append(ColorizeDescription(line[descStart..]));
        }
        else
        {
            result.Append(ColorizeOptionPart(line));
        }

        return result.ToString();
    }

    private static string ColorizeOptionPart(string text)
    {
        var result = new System.Text.StringBuilder(text.Length + 32);
        var i = 0;

        while (i < text.Length)
        {
            // Colorize option flags
            if (text[i] == '-' && (i == 0 || text[i - 1] == ' ' || text[i - 1] == ','))
            {
                var start = i;
                while (i < text.Length && text[i] != ' ' && text[i] != ',' && text[i] != '=' && text[i] != '[' && text[i] != '<')
                {
                    i++;
                }
                result.Append(Cyan).Append(text[start..i]).Append(Reset);
                continue;
            }

            // Colorize argument placeholders <...>
            if (text[i] == '<')
            {
                var start = i;
                var end = text.IndexOf('>', i);
                if (end > i)
                {
                    result.Append(Magenta).Append(text[start..(end + 1)]).Append(Reset);
                    i = end + 1;
                    continue;
                }
            }

            // Colorize optional argument markers [...]
            if (text[i] == '[')
            {
                var start = i;
                var end = text.IndexOf(']', i);
                if (end > i)
                {
                    result.Append(Magenta).Append(text[start..(end + 1)]).Append(Reset);
                    i = end + 1;
                    continue;
                }
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

    private static string ColorizeDescription(string text)
    {
        var result = new System.Text.StringBuilder(text.Length + 64);
        var i = 0;

        while (i < text.Length)
        {
            // Colorize quoted strings "..."
            if (text[i] == '"')
            {
                var start = i;
                var end = text.IndexOf('"', i + 1);
                if (end > i)
                {
                    result.Append(Red).Append(text[start..(end + 1)]).Append(Reset);
                    i = end + 1;
                    continue;
                }
            }

            // Colorize parenthetical content (...)
            if (text[i] == '(')
            {
                var start = i;
                var depth = 1;
                var j = i + 1;
                while (j < text.Length && depth > 0)
                {
                    if (text[j] == '(') depth++;
                    else if (text[j] == ')') depth--;
                    j++;
                }
                if (depth == 0)
                {
                    result.Append(Dim).Append(text[start..j]).Append(Reset);
                    i = j;
                    continue;
                }
            }

            // Colorize angle bracket placeholders <...>
            if (text[i] == '<')
            {
                var start = i;
                var end = text.IndexOf('>', i);
                if (end > i)
                {
                    result.Append(Magenta).Append(text[start..(end + 1)]).Append(Reset);
                    i = end + 1;
                    continue;
                }
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

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

                options.DelimitedCommand = args[(i + 1)..];
                options.Command = string.Join(' ', options.DelimitedCommand);
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
            var requiresValue = value is null && RequiresValue(name);
            var optionalWindowValue =
                value is null
                && (
                    string.Equals(name, "-d", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "--window", StringComparison.OrdinalIgnoreCase)
                )
                && i + 1 < args.Length
                && IsWindowStyleValue(args[i + 1]);

            var optionalVerboseValue =
                value is null
                && string.Equals(name, "--verbose", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && IsVerboseLogPathValue(args[i + 1]);

            if (requiresValue && i + 1 >= args.Length)
            {
                error = $"Missing value for option: {name}";
                return false;
            }

            if (requiresValue || optionalWindowValue || optionalVerboseValue)
            {
                // Value is required, or optional window/verbose value was provided.
                i++;
                value = args[i];
            }
            // else: value stays null - ApplyOption uses defaults

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

            // --mask: consume all following non-option tokens as additional mask patterns.
            if (
                string.Equals(name, "--mask", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                while (
                    i + 1 < args.Length
                    && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                    && !string.Equals(args[i + 1], "--", StringComparison.Ordinal)
                )
                {
                    i++;
                    options.MaskPatterns.Add(args[i]);
                }
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
            && !string.Equals(name, "--video", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--mask", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--verbose", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--no-colorenv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--no-delete-envs", StringComparison.OrdinalIgnoreCase)
            // -d/--window is optional-value; handled separately in the main loop
            && !string.Equals(name, "-d", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--window", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--pcmode", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--stdout", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "-i", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "--interactive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVerboseLogPathValue(string token) =>
        !token.StartsWith("-", StringComparison.Ordinal)
        && !string.Equals(token, "--", StringComparison.Ordinal)
        && (
            token.Contains('/', StringComparison.Ordinal)
            || token.Contains('\\', StringComparison.Ordinal)
            || token.StartsWith(".", StringComparison.Ordinal)
            || token.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || token.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsWindowStyleValue(string token) =>
        string.Equals(token, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "macos", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "windows", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "macos-pc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "windows-pc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "transparent", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || token.Contains('/')
        || token.Contains('\\');

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

    private static bool IsLengthAdjustValue(string value) =>
        string.Equals(value, "spacing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "spacingAndGlyphs", StringComparison.OrdinalIgnoreCase);
}
