using System;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleToSvg.Cli;

public static class AnsiHelpFormatter
{
    private const string Reset = "\x1b[0m";
    private const string BoldYellow = "\x1b[1;33m";
    private const string Cyan = "\x1b[36m";
    private const string Magenta = "\x1b[35m";

    /// <summary>Applies color to generated headings, option names, and placeholders.</summary>
    public static string Colorize(string text)
    {
        var result = new StringBuilder(text.Length + 128);
        var lines = text.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
                result.Append('\n');

            var line = lines[lineIndex];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.EndsWith(':') && !trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                result.Append(BoldYellow).Append(line).Append(Reset);
            }
            else
            {
                result.Append(line[..indent]).Append(ColorizeSymbols(trimmed));
            }
        }
        return result.ToString();
    }

    private static string ColorizeSymbols(string value)
    {
        var result = new StringBuilder(value.Length + 32);
        for (var index = 0; index < value.Length; )
        {
            if (value[index] == '-' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                var end = index + 1;
                while (end < value.Length && !char.IsWhiteSpace(value[end]) && value[end] != ',')
                    end++;
                result.Append(Cyan).Append(value[index..end]).Append(Reset);
                index = end;
            }
            else if (value[index] == '<')
            {
                var end = value.IndexOf('>', index);
                if (end >= 0)
                {
                    result.Append(Magenta).Append(value[index..(end + 1)]).Append(Reset);
                    index = end + 1;
                }
                else
                {
                    result.Append(value[index++]);
                }
            }
            else
            {
                result.Append(value[index++]);
            }
        }
        return result.ToString();
    }
}

internal sealed class ColoredHelpAction : SynchronousCommandLineAction
{
    public override int Invoke(ParseResult parseResult) => Write(parseResult);

    public override bool ClearsParseErrors => true;

    public static int Write(ParseResult parseResult)
    {
        var help = ConsoleToSvgCommandLine.FormatHelp(parseResult);
        parseResult.InvocationConfiguration.Output.Write(AnsiHelpFormatter.Colorize(help));
        return 0;
    }
}
