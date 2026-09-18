using System;
using System.Text;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Windows command-line quoting for PTY spawning. The backend joins
/// <c>CommandLine</c> verbatim (<c>VerbatimCommandLine = true</c>), so every
/// argument must be pre-quoted here.
/// </summary>
internal static class PtyCommandLine
{
    /// <summary>
    /// Pre-quotes each argument. The <c>cmd.exe /c</c> payload is wrapped in
    /// plain quotes (cmd.exe does not understand C-runtime <c>\"</c> escaping);
    /// all other arguments use C-runtime quoting.
    /// </summary>
    internal static string[] QuoteArgs(string app, string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return [];
        }

        var result = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            result[i] = QuoteWindowsArg(args[i], isCmdPayload: IsCmdPayload(app, args, i));
        }

        return result;
    }

    private static bool IsCmdPayload(string app, string[] args, int index) =>
        index == args.Length - 1
        && args.Length >= 3
        && string.Equals(app, "cmd.exe", StringComparison.OrdinalIgnoreCase)
        && string.Equals(args[index - 1], "/c", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Quotes a <c>cmd.exe /s /c</c> payload. cmd.exe does not understand the
    /// C-runtime <c>\"</c> escaping that <see cref="QuoteWindowsArg"/> produces
    /// (it would leak literal backslashes into the command); with
    /// <c>/s</c>, cmd strips the outermost quotes and keeps the interior
    /// verbatim, so a plain wrap is both sufficient and correct.
    /// </summary>
    private static string QuoteCmdPayload(string value) => "\"" + value + "\"";

    private static string QuoteWindowsArg(string value, bool isCmdPayload = false)
    {
        if (isCmdPayload)
        {
            return QuoteCmdPayload(value);
        }

        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var needsQuotes = value.IndexOfAny([' ', '\t', '"']) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(ch);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
