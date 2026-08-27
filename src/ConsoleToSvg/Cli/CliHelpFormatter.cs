using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleToSvg.Cli;

internal static class CliHelpFormatter
{
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Dim = "\x1b[2m";
    private const string Green = "\x1b[32m";
    private const string Cyan = "\x1b[36m";
    private const string Yellow = "\x1b[33m";
    private const string Magenta = "\x1b[35m";

    public static string Format(string help) =>
        ShouldColorize() ? Colorize(help) : ReorderOptions(help);

    public static void Write(string help)
    {
        var formatted = Format(help);
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(formatted);
            return;
        }

        var less = FindLess();
        if (less is null)
        {
            Console.WriteLine(formatted);
            return;
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"console2svg-help-{Environment.ProcessId}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryFile = Path.Combine(temporaryDirectory, Path.GetRandomFileName());
        try
        {
            File.WriteAllText(temporaryFile, formatted);
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = less,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.ArgumentList.Add("-R");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(temporaryFile);
            process.Start();
            process.WaitForExit();
        }
        catch
        {
            Console.WriteLine(formatted);
        }
        finally
        {
            try
            {
                File.Delete(temporaryFile);
                Directory.Delete(temporaryDirectory);
            }
            catch
            {
                // Help has already been displayed; cleanup failure is non-fatal.
            }
        }
    }

    internal static string Colorize(string help)
    {
        help = ReorderOptions(help);
        var result = new StringBuilder(help.Length + 512);
        var lines = help.Split('\n');
        var section = HelpSection.None;

        for (var index = 0; index < lines.Length; index++)
        {
            if (index != 0)
            {
                result.Append('\n');
            }

            var line = lines[index];
            var trimmed = line.TrimStart();
            if (index == 0 && line.StartsWith("console2svg ", StringComparison.Ordinal))
            {
                result.Append(Bold).Append(Green).Append(line).Append(Reset);
                continue;
            }

            var nextSection = GetSection(trimmed);
            if (nextSection != HelpSection.None)
            {
                section = nextSection;
                if (section == HelpSection.Usage)
                {
                    ColorizeUsage(result, line);
                }
                else
                {
                    result.Append(Bold).Append(Yellow).Append(line).Append(Reset);
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                result.Append(line);
                continue;
            }

            switch (section)
            {
                case HelpSection.Commands:
                    ColorizeCommand(result, line);
                    break;
                case HelpSection.Options when trimmed.StartsWith("-", StringComparison.Ordinal):
                    ColorizeSyntaxLine(result, line);
                    break;
                case HelpSection.Arguments when trimmed.StartsWith("[", StringComparison.Ordinal):
                    ColorizeSyntaxLine(result, line);
                    break;
                default:
                    result.Append(ColorizeDescription(line));
                    break;
            }
        }

        return result.ToString();
    }

    internal static string ReorderOptions(string help)
    {
        var lines = help.Split('\n');
        for (var start = 0; start < lines.Length; start++)
        {
            if (!string.Equals(lines[start].Trim(), "Options:", StringComparison.Ordinal))
            {
                continue;
            }

            var end = start + 1;
            while (
                end < lines.Length
                && (
                    string.IsNullOrWhiteSpace(lines[end])
                    || lines[end].StartsWith(' ')
                )
            )
            {
                end++;
            }

            var shorthand = new List<string>();
            var longhand = new List<string>();
            for (var index = start + 1; index < end; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (trimmed.Length >= 2 && trimmed[0] == '-' && trimmed[1] != '-')
                {
                    shorthand.Add(lines[index]);
                }
                else
                {
                    longhand.Add(lines[index]);
                }
            }

            var destination = start + 1;
            foreach (var line in shorthand)
            {
                lines[destination++] = line;
            }
            foreach (var line in longhand)
            {
                lines[destination++] = line;
            }
            start = end - 1;
        }

        return string.Join('\n', lines);
    }

    private static bool ShouldColorize()
    {
        if (
            Console.IsOutputRedirected
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
        )
        {
            return false;
        }

        return !string.Equals(
            Environment.GetEnvironmentVariable("TERM"),
            "dumb",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string? FindLess()
    {
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "less.exe"
            : "less";
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var directory in path.Split(separator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static HelpSection GetSection(string line)
    {
        return line switch
        {
            "Arguments:" => HelpSection.Arguments,
            "Options:" => HelpSection.Options,
            "Commands:" => HelpSection.Commands,
            _ when line.StartsWith("Usage:", StringComparison.Ordinal) => HelpSection.Usage,
            _ => HelpSection.None,
        };
    }

    private static void ColorizeUsage(StringBuilder result, string line)
    {
        var colon = line.IndexOf(':');
        result
            .Append(Bold)
            .Append(Yellow)
            .Append(line.AsSpan(0, colon + 1))
            .Append(Reset);
        ColorizeSyntax(result, line.AsSpan(colon + 1));
    }

    private static void ColorizeCommand(StringBuilder result, string line)
    {
        var trimmedStart = line.Length - line.TrimStart().Length;
        var commandEnd = FindDescriptionStart(line, trimmedStart);
        if (commandEnd < 0)
        {
            result.Append(line);
            return;
        }

        result
            .Append(line.AsSpan(0, trimmedStart))
            .Append(Bold)
            .Append(Cyan)
            .Append(line.AsSpan(trimmedStart, commandEnd - trimmedStart))
            .Append(Reset)
            .Append(ColorizeDescription(line[commandEnd..]));
    }

    private static void ColorizeSyntaxLine(StringBuilder result, string line)
    {
        var descriptionStart = FindDescriptionStart(
            line,
            line.Length - line.TrimStart().Length
        );
        if (descriptionStart < 0)
        {
            ColorizeSyntax(result, line);
            return;
        }

        ColorizeSyntax(result, line.AsSpan(0, descriptionStart));
        result.Append(ColorizeDescription(line[descriptionStart..]));
    }

    private static int FindDescriptionStart(string line, int start)
    {
        for (var index = start; index + 1 < line.Length; index++)
        {
            if (line[index] == ' ' && line[index + 1] == ' ')
            {
                var next = index + 2;
                while (next < line.Length && line[next] == ' ')
                {
                    next++;
                }

                if (next < line.Length)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static void ColorizeSyntax(StringBuilder result, ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '-' && IsTokenStart(text, index))
            {
                var end = index + 1;
                while (
                    end < text.Length
                    && text[end] is not ' ' and not ',' and not '<' and not '['
                )
                {
                    end++;
                }
                result.Append(Cyan).Append(text[index..end]).Append(Reset);
                index = end;
                continue;
            }

            if (text[index] == '<' && TryFindClosing(text, index, '>', out var angleEnd))
            {
                result.Append(Magenta).Append(text[index..(angleEnd + 1)]).Append(Reset);
                index = angleEnd + 1;
                continue;
            }

            if (text[index] == '[' && TryFindClosing(text, index, ']', out var bracketEnd))
            {
                result.Append(Dim).Append(text[index..(bracketEnd + 1)]).Append(Reset);
                index = bracketEnd + 1;
                continue;
            }

            result.Append(text[index++]);
        }
    }

    private static string ColorizeDescription(string text)
    {
        var result = new StringBuilder(text.Length + 32);
        var span = text.AsSpan();
        for (var index = 0; index < span.Length;)
        {
            if (span[index] == '[' && TryFindClosing(span, index, ']', out var end))
            {
                result.Append(Dim).Append(span[index..(end + 1)]).Append(Reset);
                index = end + 1;
                continue;
            }

            if (span[index] == '<' && TryFindClosing(span, index, '>', out end))
            {
                result.Append(Magenta).Append(span[index..(end + 1)]).Append(Reset);
                index = end + 1;
                continue;
            }

            result.Append(span[index++]);
        }

        return result.ToString();
    }

    private static bool IsTokenStart(ReadOnlySpan<char> text, int index) =>
        index == 0 || text[index - 1] is ' ' or ',' or '|';

    private static bool TryFindClosing(
        ReadOnlySpan<char> text,
        int start,
        char closing,
        out int end
    )
    {
        end = text[(start + 1)..].IndexOf(closing);
        if (end < 0)
        {
            return false;
        }

        end += start + 1;
        return true;
    }

    private enum HelpSection
    {
        None,
        Usage,
        Arguments,
        Options,
        Commands,
    }
}
