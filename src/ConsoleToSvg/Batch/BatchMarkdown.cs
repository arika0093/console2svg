using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleToSvg.Batch;

/// <summary>Marker comment style.</summary>
public enum BatchMarkerKind
{
    Html,
    Mdx,
}

/// <summary>A single c2s block parsed from markdown.</summary>
public sealed record BatchParsedJob(
    int Index,
    int MarkerStart,
    int MarkerEnd,
    int MarkerLine,
    BatchMarkerKind Kind,
    string Setup,
    string Capture,
    string Teardown,
    int? Width,
    int? Height,
    bool WithCommand,
    string? Window,
    bool WindowExplicit,
    bool Video,
    double? Timeout,
    string OutputRelative,
    bool OutputAuto,
    string Alt
);

/// <summary>A parse problem with a 1-based line number.</summary>
public sealed record BatchParseError(int Line, string Message);

/// <summary>Result of parsing a markdown document.</summary>
public sealed record BatchParseResult(
    IReadOnlyList<BatchParsedJob> Jobs,
    IReadOnlyList<BatchParseError> Errors
);

/// <summary>Link target resolved for a parsed job.</summary>
public sealed record BatchLink(BatchParsedJob Job, string RelativeLink);

/// <summary>
/// Parses <c>c2s::</c> markers in markdown/MDX and rewrites image links.
/// Pure string processing; no process execution here so it stays unit-testable.
/// </summary>
public static class BatchMarkdown
{
    public const string CaptureMarker = "__C2S_CAPTURE_START__";

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex HtmlMarkerPattern = new(
        @"<!--\s*(c2s|console2svg)::(.*?)-->",
        RegexOptions.Singleline | RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex MdxMarkerPattern = new(
        @"\{\s*/\*\s*(c2s|console2svg)::(.*?)\*/\s*\}",
        RegexOptions.Singleline | RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex OptionsSplitterPattern = new(
        @"(?:^|\s)--(?:\s|$)",
        RegexOptions.Singleline | RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex CodePlaceholderPattern = new(
        @"\{code(?:(?::(\d+))|(?:/([A-Za-z0-9#+_\-]+)(?::(\d+))?))?\}",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex ImageLinePattern = new(
        @"^[ \t]*!\[[^\]]*\]\([^)]*\)[ \t]*$",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly HashSet<string> BashLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "sh",
        "shell",
        "console",
        "zsh",
        "ksh",
        "dash",
        "ps",
        "powershell",
        "pwsh",
        "cmd",
        "batch",
        "terminal",
        "sh-session",
        "shell-session",
    };

    private static readonly Dictionary<string, string> LanguageAliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["c#"] = "csharp",
        ["cs"] = "csharp",
        ["js"] = "javascript",
        ["py"] = "python",
        ["ts"] = "typescript",
        ["ps1"] = "powershell",
    };

    private sealed record CodeFence(string Language, string Content, int Start, int End);

    private sealed record FoundMarker(BatchMarkerKind Kind, int Start, int End, string Inner);

    /// <summary>Parses all c2s jobs in a markdown document.</summary>
    public static BatchParseResult Parse(string markdown, string mdPath)
    {
        var fences = FindCodeFences(markdown);
        var markers = FindMarkers(markdown);
        var jobs = new List<BatchParsedJob>();
        var errors = new List<BatchParseError>();
        var mdBase = Path.GetFileNameWithoutExtension(mdPath);
        if (string.IsNullOrWhiteSpace(mdBase))
        {
            mdBase = "doc";
        }

        var index = 0;
        foreach (var marker in markers)
        {
            index++;
            var line = GetLineNumber(markdown, marker.Start);
            var parsed = ParseMarker(marker, fences, index, mdBase, line, errors);
            if (parsed is not null)
            {
                jobs.Add(parsed);
            }
        }

        return new BatchParseResult(jobs, errors);
    }

    /// <summary>Validates a marker -o value without touching the filesystem.</summary>
    public static bool TryValidateOutputRelative(
        string value,
        out string normalized,
        out string? error
    )
    {
        normalized = value.Trim().Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        error = null;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "-o value must not be empty.";
            return false;
        }

        if (
            Path.IsPathRooted(normalized)
            || normalized.StartsWith('/', StringComparison.Ordinal)
            || normalized.StartsWith("~", StringComparison.Ordinal)
        )
        {
            error = $"-o must be a relative file name (got '{value}').";
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is ".." || (segment.Contains(':') && segment.Length == 2))
            {
                error = $"-o must stay inside the assets directory (got '{value}').";
                return false;
            }

            if (string.IsNullOrWhiteSpace(segment))
            {
                error = $"-o contains an empty path segment (got '{value}').";
                return false;
            }
        }

        if (
            !string.Equals(
                Path.GetExtension(normalized),
                ".svg",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            error = $"batch MVP only supports .svg output (got '{value}').";
            return false;
        }

        return true;
    }

    /// <summary>Computes the --cached fingerprint for a job.</summary>
    public static string ComputeJobHash(
        string setup,
        string capture,
        string teardown,
        string optionsFingerprint,
        string appVersion
    )
    {
        var payload =
            setup
            + "\n---\n"
            + capture
            + "\n---\n"
            + teardown
            + "\n---\n"
            + optionsFingerprint
            + "\n---\n"
            + appVersion;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Rewrites (or inserts) image links for executed jobs.</summary>
    public static string RewriteLinks(string markdown, IReadOnlyList<BatchLink> links)
    {
        if (links.Count == 0)
        {
            return markdown;
        }

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // Apply from the end so earlier offsets stay valid.
        var ordered = new List<BatchLink>(links);
        ordered.Sort((a, b) => b.Job.MarkerStart.CompareTo(a.Job.MarkerStart));
        var result = markdown;
        foreach (var link in ordered)
        {
            result = ApplyLink(result, link, newline);
        }

        return result;
    }

    private static string ApplyLink(string markdown, BatchLink link, string newline)
    {
        var imageLine = $"![{link.Job.Alt}]({link.RelativeLink})";
        var markerLineEnd = markdown.IndexOf('\n', link.Job.MarkerEnd);
        if (markerLineEnd < 0)
        {
            return markdown + newline + imageLine + newline;
        }

        var cursor = markerLineEnd + 1;
        while (true)
        {
            var lineEnd = markdown.IndexOf('\n', cursor);
            var line = lineEnd < 0 ? markdown[cursor..] : markdown[cursor..lineEnd];
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (lineEnd < 0)
                {
                    return markdown[..cursor] + imageLine + newline;
                }

                cursor = lineEnd + 1;
                continue;
            }

            if (ImageLinePattern.IsMatch(line.TrimEnd('\r')))
            {
                var before = markdown[..cursor];
                var hasNewline = lineEnd >= 0;
                var after = hasNewline ? markdown[(lineEnd + 1)..] : string.Empty;
                var suffix = line.EndsWith("\r", StringComparison.Ordinal) ? "\r" : string.Empty;
                return before + imageLine + suffix + (hasNewline ? newline + after : string.Empty);
            }

            // Next content is not an image: insert right after the marker line.
            var insertAt = markerLineEnd + 1;
            return markdown[..insertAt] + imageLine + newline + markdown[insertAt..];
        }
    }

    private static BatchParsedJob? ParseMarker(
        FoundMarker marker,
        IReadOnlyList<CodeFence> fences,
        int index,
        string mdBase,
        int line,
        List<BatchParseError> errors
    )
    {
        var split = OptionsSplitterPattern.Match(marker.Inner);
        var optionsText = split.Success ? marker.Inner[..split.Index].Trim() : marker.Inner.Trim();
        var scriptText = split.Success
            ? marker.Inner[(split.Index + split.Length)..].Trim('\r', '\n', ' ', '\t').TrimEnd()
            : string.Empty;
        var hasExplicitScript = split.Success;

        if (
            !TryParseOptions(
                optionsText,
                line,
                errors,
                out var width,
                out var height,
                out var withCommand,
                out var window,
                out var windowExplicit,
                out var video,
                out var timeout,
                out var outputRelative
            )
        )
        {
            return null;
        }

        string setup = string.Empty;
        string capture;
        string teardown = string.Empty;

        if (!hasExplicitScript || scriptText.Length == 0)
        {
            var fence = FindPrecedingFence(fences, marker.Start, bashOnly: true);
            if (fence is null)
            {
                errors.Add(
                    new BatchParseError(
                        line,
                        "no command found: add `-- <command>` or a preceding bash code block."
                    )
                );
                return null;
            }

            capture = fence.Content.TrimEnd();
            if (capture.Length == 0)
            {
                errors.Add(new BatchParseError(line, "preceding code block is empty."));
                return null;
            }
        }
        else
        {
            var sections = SplitSections(scriptText);
            if (sections is null)
            {
                errors.Add(
                    new BatchParseError(
                        line,
                        "'---' separators: use 0, 1, or 2 (setup --- capture --- teardown)."
                    )
                );
                return null;
            }

            if (sections.Count == 1)
            {
                var single = sections[0].Trim();
                if (single.Contains('\n'))
                {
                    errors.Add(
                        new BatchParseError(
                            line,
                            "multi-line scripts require '---' separators (setup --- capture)."
                        )
                    );
                    return null;
                }

                capture = single;
            }
            else if (sections.Count == 2)
            {
                setup = sections[0].Trim('\r', '\n', ' ', '\t');
                capture = sections[1].Trim();
            }
            else
            {
                setup = sections[0].Trim('\r', '\n', ' ', '\t');
                capture = sections[1].Trim();
                teardown = sections[2].Trim('\r', '\n', ' ', '\t');
            }

            if (capture.Length == 0)
            {
                errors.Add(new BatchParseError(line, "capture section after '---' is empty."));
                return null;
            }
        }

        setup = ExpandPlaceholders(setup, fences, marker.Start, line, errors);
        capture = ExpandPlaceholders(capture, fences, marker.Start, line, errors);
        teardown = ExpandPlaceholders(teardown, fences, marker.Start, line, errors);
        if (HasErrorAtLine(errors, line))
        {
            return null;
        }

        var outputAuto = string.IsNullOrWhiteSpace(outputRelative);
        string output;
        if (outputAuto)
        {
            output = $"{mdBase}-{index}.svg";
        }
        else if (
            TryValidateOutputRelative(outputRelative!, out var normalized, out var outputError)
        )
        {
            output = normalized;
        }
        else
        {
            errors.Add(new BatchParseError(line, outputError!));
            return null;
        }

        var alt = FirstLine(capture);
        return new BatchParsedJob(
            index,
            marker.Start,
            marker.End,
            line,
            marker.Kind,
            setup,
            capture,
            teardown,
            width,
            height,
            withCommand,
            window,
            windowExplicit,
            video,
            timeout,
            output,
            outputAuto,
            alt
        );
    }

    private static bool HasErrorAtLine(List<BatchParseError> errors, int line)
    {
        foreach (var error in errors)
        {
            if (error.Line == line)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string>? SplitSections(string script)
    {
        var lines = script.Split('\n');
        var sections = new List<string> { new StringBuilder().ToString() };
        var builders = new List<StringBuilder> { new() };
        foreach (var rawLine in lines)
        {
            if (rawLine.Trim().TrimEnd('\r') == "---")
            {
                builders.Add(new StringBuilder());
                continue;
            }

            builders[^1].Append(rawLine);
            builders[^1].Append('\n');
        }

        sections.Clear();
        foreach (var builder in builders)
        {
            sections.Add(builder.ToString());
        }

        return sections.Count is 1 or 2 or 3 ? sections : null;
    }

    private static string ExpandPlaceholders(
        string script,
        IReadOnlyList<CodeFence> fences,
        int markerStart,
        int line,
        List<BatchParseError> errors
    )
    {
        if (script.Length == 0 || !script.Contains("{code", StringComparison.Ordinal))
        {
            return script;
        }

        return CodePlaceholderPattern.Replace(
            script,
            match =>
            {
                var indexGroup = match.Groups[1].Success ? match.Groups[1] : match.Groups[3];
                var langGroup = match.Groups[2];
                var skip = 0;
                if (indexGroup.Success && !int.TryParse(indexGroup.Value, out skip))
                {
                    errors.Add(new BatchParseError(line, $"invalid placeholder '{match.Value}'."));
                    return match.Value;
                }

                string? content = null;
                if (langGroup.Success)
                {
                    content = FindCodeByLanguage(fences, markerStart, langGroup.Value, skip);
                    if (content is null)
                    {
                        errors.Add(
                            new BatchParseError(
                                line,
                                $"placeholder '{match.Value}' has no matching '{langGroup.Value}' code block."
                            )
                        );
                        return match.Value;
                    }
                }
                else
                {
                    content = FindCodeByIndex(fences, markerStart, skip, bashOnly: false);
                    if (content is null)
                    {
                        errors.Add(
                            new BatchParseError(
                                line,
                                $"placeholder '{match.Value}' has no preceding code block."
                            )
                        );
                        return match.Value;
                    }
                }

                return content.TrimEnd();
            }
        );
    }

    private static string? FindCodeByIndex(
        IReadOnlyList<CodeFence> fences,
        int markerStart,
        int skip,
        bool bashOnly
    )
    {
        var seen = 0;
        for (var i = fences.Count - 1; i >= 0; i--)
        {
            var fence = fences[i];
            if (fence.End > markerStart)
            {
                continue;
            }

            var isBash = IsBashLanguage(fence.Language);
            if (bashOnly && !isBash)
            {
                continue;
            }

            if (!bashOnly && isBash)
            {
                continue;
            }

            if (seen == skip)
            {
                return fence.Content;
            }

            seen++;
        }

        return null;
    }

    private static string? FindCodeByLanguage(
        IReadOnlyList<CodeFence> fences,
        int markerStart,
        string language,
        int skip
    )
    {
        var wanted = NormalizeLanguage(language);
        var seen = 0;
        for (var i = fences.Count - 1; i >= 0; i--)
        {
            var fence = fences[i];
            if (fence.End > markerStart)
            {
                continue;
            }

            if (!string.Equals(NormalizeLanguage(fence.Language), wanted, StringComparison.Ordinal))
            {
                continue;
            }

            if (seen == skip)
            {
                return fence.Content;
            }

            seen++;
        }

        return null;
    }

    private static CodeFence? FindPrecedingFence(
        IReadOnlyList<CodeFence> fences,
        int markerStart,
        bool bashOnly
    )
    {
        for (var i = fences.Count - 1; i >= 0; i--)
        {
            var fence = fences[i];
            if (fence.End > markerStart)
            {
                continue;
            }

            if (bashOnly && !IsBashLanguage(fence.Language))
            {
                continue;
            }

            return fence;
        }

        return null;
    }

    private static bool IsBashLanguage(string language) => BashLanguages.Contains(language);

    private static string NormalizeLanguage(string language)
    {
        var trimmed = language.Trim();
        return LanguageAliases.TryGetValue(trimmed, out var mapped)
            ? mapped
            : trimmed.ToLowerInvariant();
    }

    private static List<CodeFence> FindCodeFences(string markdown)
    {
        var fences = new List<CodeFence>();
        var lines = markdown.Split('\n');
        var offset = 0;
        var inFence = false;
        var language = string.Empty;
        var contentStart = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var stripped = line.TrimEnd('\r');
            if (!inFence && stripped.StartsWith("```", StringComparison.Ordinal))
            {
                var info = stripped[3..].Trim();
                var space = info.IndexOfAny([' ', '\t']);
                language = (space < 0 ? info : info[..space]).Trim().ToLowerInvariant();
                inFence = true;
                contentStart = offset + line.Length + 1;
            }
            else if (inFence && stripped.Trim() == "```")
            {
                var content = markdown[contentStart..offset];
                fences.Add(
                    new CodeFence(language, content.TrimEnd('\r', '\n'), contentStart, offset)
                );
                inFence = false;
                language = string.Empty;
            }

            offset += line.Length + 1;
        }

        return fences;
    }

    private static List<FoundMarker> FindMarkers(string markdown)
    {
        var found = new List<FoundMarker>();
        foreach (System.Text.RegularExpressions.Match match in HtmlMarkerPattern.Matches(markdown))
        {
            found.Add(
                new FoundMarker(
                    BatchMarkerKind.Html,
                    match.Index,
                    match.Index + match.Length,
                    match.Groups[2].Value
                )
            );
        }

        foreach (System.Text.RegularExpressions.Match match in MdxMarkerPattern.Matches(markdown))
        {
            found.Add(
                new FoundMarker(
                    BatchMarkerKind.Mdx,
                    match.Index,
                    match.Index + match.Length,
                    match.Groups[2].Value
                )
            );
        }

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    private static bool TryParseOptions(
        string optionsText,
        int line,
        List<BatchParseError> errors,
        out int? width,
        out int? height,
        out bool withCommand,
        out string? window,
        out bool windowExplicit,
        out bool video,
        out double? timeout,
        out string? outputRelative
    )
    {
        width = null;
        height = null;
        withCommand = false;
        window = null;
        windowExplicit = false;
        video = false;
        timeout = null;
        outputRelative = null;

        var tokens = Tokenize(optionsText);
        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            switch (token)
            {
                case "-w" or "--width":
                    if (!TakeInt(tokens, ref i, line, errors, "--width", out var w))
                    {
                        return false;
                    }

                    width = w;
                    break;
                case "-h" or "--height":
                    if (!TakeInt(tokens, ref i, line, errors, "--height", out var h))
                    {
                        return false;
                    }

                    height = h;
                    break;
                case "-o" or "--out":
                    if (i + 1 >= tokens.Count)
                    {
                        errors.Add(new BatchParseError(line, "-o requires a file name."));
                        return false;
                    }

                    outputRelative = tokens[++i];
                    i++;
                    break;
                case "-c" or "--with-command":
                    withCommand = true;
                    i++;
                    break;
                case "-d" or "--window":
                    windowExplicit = true;
                    if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                    {
                        window = tokens[++i];
                    }
                    else
                    {
                        window = "macos";
                    }

                    i++;
                    break;
                case "-v" or "--video":
                    video = true;
                    i++;
                    break;
                case "--mode":
                    if (i + 1 >= tokens.Count)
                    {
                        errors.Add(new BatchParseError(line, "--mode requires image or video."));
                        return false;
                    }

                    var mode = tokens[++i].ToLowerInvariant();
                    if (mode is not "image" and not "video")
                    {
                        errors.Add(new BatchParseError(line, "--mode must be image or video."));
                        return false;
                    }

                    video = mode == "video";
                    i++;
                    break;
                case "--timeout":
                    if (
                        i + 1 >= tokens.Count
                        || !double.TryParse(
                            tokens[i + 1],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var seconds
                        )
                        || seconds <= 0
                    )
                    {
                        errors.Add(new BatchParseError(line, "--timeout must be greater than 0."));
                        return false;
                    }

                    timeout = seconds;
                    i += 2;
                    break;
                default:
                    errors.Add(new BatchParseError(line, $"unsupported marker option '{token}'."));
                    return false;
            }
        }

        return true;
    }

    private static bool TakeInt(
        List<string> tokens,
        ref int index,
        int line,
        List<BatchParseError> errors,
        string name,
        out int value
    )
    {
        value = 0;
        if (index + 1 >= tokens.Count || !int.TryParse(tokens[index + 1], out value) || value <= 0)
        {
            errors.Add(new BatchParseError(line, $"{name} must be greater than 0."));
            return false;
        }

        index += 2;
        return true;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        char? quote = null;
        var hasToken = false;

        void Flush()
        {
            if (hasToken)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
                hasToken = false;
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quote.HasValue)
            {
                if (ch == quote.Value)
                {
                    quote = null;
                }
                else
                {
                    builder.Append(ch);
                    hasToken = true;
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            builder.Append(ch);
            hasToken = true;
        }

        Flush();
        return tokens;
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                var single = trimmed.Replace("]", ")").Replace("|", "/");
                return single.Length > 80 ? single[..80] : single;
            }
        }

        return "console2svg";
    }

    private static int GetLineNumber(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
