using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConsoleToSvg.Cli;
using VYaml.Parser;

namespace ConsoleToSvg.Batch;

public enum BatchMarkerKind
{
    Html,
    Mdx,
}

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
    string? OutputRelative,
    string? ExistingLinkTarget,
    string Alt,
    AppOptions CaptureOptions
)
{
    public bool OutputAuto => OutputRelative is null;
}

public sealed record BatchParseError(int Line, string Message);

public sealed record BatchParseResult(
    IReadOnlyList<BatchParsedJob> Jobs,
    IReadOnlyList<BatchParseError> Errors
);

public sealed record BatchLink(BatchParsedJob Job, string RelativeLink);

/// <summary>
/// Parses c2s markers in Markdown/MDX and rewrites their associated image links.
/// This class performs string processing only; commands are executed elsewhere.
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
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex CodePlaceholderPattern = new(
        @"\{code:([A-Za-z][A-Za-z0-9_-]*)\}",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex AnyCodePlaceholderPattern = new(
        @"\{code[^}]*\}",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex FenceOpeningPattern = new(
        @"^( {0,3})(`{3,}|~{3,})(.*)$",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex FenceIdPattern = new(
        @"(?:^|\s)c2s-id=([A-Za-z][A-Za-z0-9_-]*)(?=\s|$)",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex ImageLinePattern = new(
        "^[ \\t]*!\\[(?<alt>[^\\]]*)\\]\\((?<target><[^>]+>|[^)\\s]+)(?:\\s+(?:\"[^\"]*\"|'[^']*'|\\([^)]*\\)))?\\)[ \\t]*\\r?$",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex MarkdownImagePattern = new(
        "!\\[[^\\]]*\\]\\((?<target><[^>]+>|[^)\\s]+)(?:\\s+(?:\"[^\"]*\"|'[^']*'|\\([^)]*\\)))?\\)",
        RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex HtmlImagePattern = new(
        "<img\\s+[^>]*src=[\"'](?<target>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly Regex HtmlSourcePattern = new(
        "<source\\s+[^>]*src=[\"'](?<target>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        PatternTimeout
    );

    private static readonly HashSet<string> ShellLanguages = new(StringComparer.OrdinalIgnoreCase)
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

    private sealed record CodeFence(
        string Language,
        string? Id,
        string Content,
        int Start,
        int End,
        int Line
    );

    private sealed record FoundMarker(BatchMarkerKind Kind, int Start, int End, string Inner);

    private sealed record MarkerBody(string Setup, string Capture, string Teardown);

    public static BatchParseResult Parse(string markdown, string mdPath)
    {
        var errors = new List<BatchParseError>();
        var fences = FindCodeFences(markdown);
        ValidateFenceIds(fences, errors);
        var markers = FindMarkers(markdown);
        var jobs = new List<BatchParsedJob>();

        var index = 0;
        foreach (var marker in markers)
        {
            index++;
            var line = GetLineNumber(markdown, marker.Start);
            var parsed = ParseMarker(markdown, marker, fences, index, line, errors);
            if (parsed is not null)
            {
                jobs.Add(parsed);
            }
        }

        return new BatchParseResult(jobs, errors);
    }

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
            || normalized.StartsWith('~')
        )
        {
            error = $"-o must be relative to the output directory (got '{value}').";
            return false;
        }

        if (
            normalized
                .Split('/')
                .Any(segment =>
                    segment is ".." || string.IsNullOrWhiteSpace(segment) || segment.Contains(':')
                )
        )
        {
            error = $"-o must stay inside the output directory (got '{value}').";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
        {
            error = $"-o must include a file extension (got '{value}').";
            return false;
        }

        return true;
    }

    private static bool TryValidateBatchCaptureOptions(AppOptions options, out string? error)
    {
        string? unsupported = null;
        if (options.Workflow != Workflow.Capture)
            unsupported = "--legacy-root";
        else if (!string.IsNullOrWhiteSpace(options.InputCastPath))
            unsupported = "--in";
        else if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            unsupported = "--save-cast";
        else if (!string.IsNullOrWhiteSpace(options.SaveFramesDir))
            unsupported = "--save-frames";
        else if (options.StdOut)
            unsupported = "--stdout";
        else if (options.EmbedDebug)
            unsupported = "--embed-debug";
        else if (options.EmbedCast)
            unsupported = "--embed-cast";
        else if (options.EmbedLogs)
            unsupported = "--embed-logs";
        else if (options.EmbedReplay)
            unsupported = "--embed-replay";
        else if (options.Verbose)
            unsupported = "--verbose";
        else if (!string.IsNullOrWhiteSpace(options.VerboseLogPath))
            unsupported = "--verbose-log";
        else if (!options.LiveServerResize)
            unsupported = "--no-resize";
        else if (options.IsMouseExplicit && options.Mouse)
            unsupported = "--mouse";

        error = unsupported is null
            ? null
            : $"unsupported marker option '{unsupported}': this capture side effect is not implemented by batch markdown.";
        return unsupported is null;
    }

    public static string RewriteLinks(string markdown, IReadOnlyList<BatchLink> links)
    {
        if (links.Count == 0)
        {
            return markdown;
        }

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var ordered = links.OrderByDescending(link => link.Job.MarkerStart);
        var result = markdown;
        foreach (var link in ordered)
        {
            result = ApplyLink(result, link, newline);
        }

        return result;
    }

    public static string RewriteAssetLinks(
        string markdown,
        IReadOnlyDictionary<string, string> replacements
    )
    {
        if (replacements.Count == 0)
        {
            return markdown;
        }

        var result = markdown;
        foreach (var pattern in new[] { MarkdownImagePattern, HtmlImagePattern, HtmlSourcePattern })
        {
            var fences = FindCodeFences(result);
            result = pattern.Replace(
                result,
                match =>
                {
                    if (fences.Any(fence => match.Index >= fence.Start && match.Index < fence.End))
                    {
                        return match.Value;
                    }

                    var target = match.Groups["target"];
                    var unwrapped = target.Value.Trim('<', '>');
                    if (!replacements.TryGetValue(unwrapped, out var replacement))
                    {
                        return match.Value;
                    }

                    var replacementTarget = target.Value.StartsWith('<')
                        ? $"<{replacement}>"
                        : replacement;
                    var targetOffset = target.Index - match.Index;
                    return match.Value[..targetOffset]
                        + replacementTarget
                        + match.Value[(targetOffset + target.Length)..];
                }
            );
        }

        return result;
    }

    public static IReadOnlyList<string> FindImageTargets(string markdown)
    {
        var fences = FindCodeFences(markdown);
        return MarkdownImagePattern
            .Matches(markdown)
            .Cast<Match>()
            .Where(match =>
                !fences.Any(fence => match.Index >= fence.Start && match.Index < fence.End)
            )
            .Select(match => match.Groups["target"].Value.Trim('<', '>'))
            .ToArray();
    }

    private static BatchParsedJob? ParseMarker(
        string markdown,
        FoundMarker marker,
        IReadOnlyList<CodeFence> fences,
        int index,
        int line,
        List<BatchParseError> errors
    )
    {
        var inner = marker.Inner.Replace("\r\n", "\n", StringComparison.Ordinal);
        var firstLineEnd = inner.IndexOf('\n');
        var header = (firstLineEnd < 0 ? inner : inner[..firstLineEnd]).Trim();
        var yaml = firstLineEnd < 0 ? string.Empty : inner[(firstLineEnd + 1)..].Trim();

        var split = OptionsSplitterPattern.Match(header);
        var optionsText = split.Success ? header[..split.Index].Trim() : header;
        var inlineCapture = split.Success
            ? header[(split.Index + split.Length)..].Trim()
            : string.Empty;

        var optionTokens = Tokenize(optionsText, line, errors);
        if (optionTokens is null)
        {
            return null;
        }

        if (
            !ConsoleToSvgCommandLine.TryParseCaptureOptions(
                optionTokens,
                out var captureOptions,
                out var hasOutput,
                out var optionError
            )
        )
        {
            errors.Add(new BatchParseError(line, optionError ?? "invalid capture options."));
            return null;
        }

        if (!TryValidateBatchCaptureOptions(captureOptions!, out var batchOptionError))
        {
            errors.Add(new BatchParseError(line, batchOptionError!));
            return null;
        }

        var outputRelative = hasOutput ? captureOptions!.OutputPath : null;

        var body = ParseYamlBody(yaml, line, errors);
        if (body is null)
        {
            return null;
        }

        if (inlineCapture.Length > 0 && body.Capture.Length > 0)
        {
            errors.Add(
                new BatchParseError(
                    line,
                    "capture is specified both after '--' and in the YAML body."
                )
            );
            return null;
        }

        var capture = inlineCapture.Length > 0 ? inlineCapture : body.Capture;
        if (capture.Length == 0)
        {
            var fence = FindImmediatelyPrecedingShellFence(markdown, fences, marker.Start);
            if (fence is null)
            {
                errors.Add(
                    new BatchParseError(
                        line,
                        "no command found: add `-- <command>`, a YAML capture value, or place the marker immediately after a shell code block."
                    )
                );
                return null;
            }

            capture = fence.Content.TrimEnd();
        }

        var setup = ExpandNamedCode(body.Setup, fences, marker.Start, line, errors);
        capture = ExpandNamedCode(capture, fences, marker.Start, line, errors);
        var teardown = ExpandNamedCode(body.Teardown, fences, marker.Start, line, errors);
        if (HasErrorAtLine(errors, line) || string.IsNullOrWhiteSpace(capture))
        {
            if (string.IsNullOrWhiteSpace(capture) && !HasErrorAtLine(errors, line))
            {
                errors.Add(new BatchParseError(line, "capture command is empty."));
            }

            return null;
        }

        string? output = null;
        if (
            !string.IsNullOrWhiteSpace(outputRelative)
            && !TryValidateOutputRelative(outputRelative, out output, out var outputError)
        )
        {
            errors.Add(new BatchParseError(line, outputError!));
            return null;
        }

        return new BatchParsedJob(
            index,
            marker.Start,
            marker.End,
            line,
            marker.Kind,
            setup,
            capture,
            teardown,
            captureOptions!.Width,
            captureOptions.Height,
            captureOptions.WithCommand,
            captureOptions.Window,
            captureOptions.IsWindowExplicit,
            captureOptions.Mode is OutputMode.Video,
            captureOptions.Timeout,
            output,
            FindAssociatedImageTarget(markdown, marker.End),
            FirstLine(capture),
            captureOptions
        );
    }

    private static MarkerBody? ParseYamlBody(
        string yaml,
        int markerLine,
        List<BatchParseError> errors
    )
    {
        if (yaml.Length == 0)
        {
            return new MarkerBody(string.Empty, string.Empty, string.Empty);
        }

        try
        {
            return ParseYamlMapping(Encoding.UTF8.GetBytes(yaml), markerLine, errors);
        }
        catch (Exception ex)
        {
            errors.Add(
                new BatchParseError(
                    markerLine,
                    $"invalid marker YAML: {ex.Message.Replace(Environment.NewLine, " ")}"
                )
            );
            return null;
        }
    }

    private static MarkerBody? ParseYamlMapping(
        byte[] yaml,
        int markerLine,
        List<BatchParseError> errors
    )
    {
        var parser = YamlParser.FromBytes(yaml);
        parser.Read();
        parser.Read();
        parser.Read();
        if (parser.CurrentEventType != ParseEventType.MappingStart)
        {
            errors.Add(new BatchParseError(markerLine, "marker YAML must be a mapping."));
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        parser.Read();
        while (parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            if (parser.CurrentEventType != ParseEventType.Scalar)
            {
                errors.Add(new BatchParseError(markerLine, "marker YAML keys must be strings."));
                return null;
            }

            var key = parser.ReadScalarAsString();
            if (key is not "setup" and not "capture" and not "teardown")
            {
                errors.Add(new BatchParseError(markerLine, $"unknown marker YAML key '{key}'."));
                return null;
            }

            if (values.ContainsKey(key))
            {
                errors.Add(new BatchParseError(markerLine, $"duplicate marker YAML key '{key}'."));
                return null;
            }

            if (parser.CurrentEventType != ParseEventType.Scalar)
            {
                errors.Add(
                    new BatchParseError(markerLine, $"marker YAML '{key}' must be a string.")
                );
                return null;
            }

            string value;
            if (parser.IsNullScalar())
            {
                value = string.Empty;
                parser.Read();
            }
            else
            {
                value = parser.ReadScalarAsString() ?? string.Empty;
            }
            values.Add(key, value.TrimEnd('\r', '\n'));
        }

        return new MarkerBody(
            values.GetValueOrDefault("setup", string.Empty),
            values.GetValueOrDefault("capture", string.Empty),
            values.GetValueOrDefault("teardown", string.Empty)
        );
    }

    private static string ExpandNamedCode(
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

        var unsupported = AnyCodePlaceholderPattern
            .Matches(script)
            .Cast<Match>()
            .FirstOrDefault(match => !CodePlaceholderPattern.IsMatch(match.Value));
        if (unsupported is not null)
        {
            errors.Add(
                new BatchParseError(
                    line,
                    $"unsupported code placeholder '{unsupported.Value}'; use '{{code:<c2s-id>}}'."
                )
            );
        }

        return CodePlaceholderPattern.Replace(
            script,
            match =>
            {
                var id = match.Groups[1].Value;
                var fence = fences.LastOrDefault(item =>
                    item.End <= markerStart && string.Equals(item.Id, id, StringComparison.Ordinal)
                );
                if (fence is null)
                {
                    errors.Add(
                        new BatchParseError(
                            line,
                            $"placeholder '{match.Value}' has no preceding code block with c2s-id={id}."
                        )
                    );
                    return match.Value;
                }

                return fence.Content.TrimEnd();
            }
        );
    }

    private static void ValidateFenceIds(
        IReadOnlyList<CodeFence> fences,
        List<BatchParseError> errors
    )
    {
        var ids = new Dictionary<string, CodeFence>(StringComparer.Ordinal);
        foreach (var fence in fences)
        {
            if (fence.Id is null)
            {
                continue;
            }

            if (ids.TryGetValue(fence.Id, out var previous))
            {
                errors.Add(
                    new BatchParseError(
                        fence.Line,
                        $"duplicate c2s-id '{fence.Id}' (first declared on line {previous.Line})."
                    )
                );
            }
            else
            {
                ids.Add(fence.Id, fence);
            }
        }
    }

    private static CodeFence? FindImmediatelyPrecedingShellFence(
        string markdown,
        IReadOnlyList<CodeFence> fences,
        int markerStart
    )
    {
        for (var i = fences.Count - 1; i >= 0; i--)
        {
            var fence = fences[i];
            if (fence.End > markerStart)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(markdown[fence.End..markerStart]))
            {
                return null;
            }

            return IsShellLanguage(fence.Language) ? fence : null;
        }

        return null;
    }

    private static List<CodeFence> FindCodeFences(string markdown)
    {
        var fences = new List<CodeFence>();
        var offset = 0;
        var lineNumber = 1;
        CodeFenceBuilder? open = null;

        while (offset < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', offset);
            var next = lineEnd < 0 ? markdown.Length : lineEnd + 1;
            var rawLine = markdown[offset..(lineEnd < 0 ? markdown.Length : lineEnd)].TrimEnd('\r');

            if (open is null)
            {
                var opening = FenceOpeningPattern.Match(rawLine);
                if (opening.Success)
                {
                    var fenceText = opening.Groups[2].Value;
                    var info = opening.Groups[3].Value.Trim();
                    var language = FirstToken(info);
                    var idMatch = FenceIdPattern.Match(info);
                    open = new CodeFenceBuilder(
                        fenceText[0],
                        fenceText.Length,
                        language,
                        idMatch.Success ? idMatch.Groups[1].Value : null,
                        offset,
                        next,
                        lineNumber
                    );
                }
            }
            else if (IsClosingFence(rawLine, open.Character, open.Length))
            {
                var content = markdown[open.ContentStart..offset].TrimEnd('\r', '\n');
                fences.Add(
                    new CodeFence(open.Language, open.Id, content, open.Start, next, open.Line)
                );
                open = null;
            }

            offset = next;
            lineNumber++;
        }

        if (open is not null)
        {
            fences.Add(
                new CodeFence(
                    open.Language,
                    open.Id,
                    markdown[open.ContentStart..].TrimEnd('\r', '\n'),
                    open.Start,
                    markdown.Length,
                    open.Line
                )
            );
        }

        return fences;
    }

    private sealed record CodeFenceBuilder(
        char Character,
        int Length,
        string Language,
        string? Id,
        int Start,
        int ContentStart,
        int Line
    );

    private static bool IsClosingFence(string line, char character, int minimumLength)
    {
        var trimmed = line.TrimStart();
        if (line.Length - trimmed.Length > 3 || trimmed.Length < minimumLength)
        {
            return false;
        }

        var count = 0;
        while (count < trimmed.Length && trimmed[count] == character)
        {
            count++;
        }

        return count >= minimumLength && string.IsNullOrWhiteSpace(trimmed[count..]);
    }

    private static List<FoundMarker> FindMarkers(string markdown)
    {
        var excludedRanges = MarkdownCodeContext.FindExcludedRanges(markdown);
        var found = new List<FoundMarker>();
        AddMarkers(HtmlMarkerPattern, BatchMarkerKind.Html);
        AddMarkers(MdxMarkerPattern, BatchMarkerKind.Mdx);
        found.Sort((left, right) => left.Start.CompareTo(right.Start));
        return found;

        void AddMarkers(Regex pattern, BatchMarkerKind kind)
        {
            foreach (Match match in pattern.Matches(markdown))
            {
                if (excludedRanges.Any(range => range.Contains(match.Index)))
                {
                    continue;
                }

                found.Add(
                    new FoundMarker(
                        kind,
                        match.Index,
                        match.Index + match.Length,
                        match.Groups[2].Value
                    )
                );
            }
        }
    }

    private static string? FindAssociatedImageTarget(string markdown, int markerEnd)
    {
        var cursor = markerEnd;
        while (cursor <= markdown.Length)
        {
            var nextLineEnd = markdown.IndexOf('\n', cursor);
            var line = nextLineEnd < 0 ? markdown[cursor..] : markdown[cursor..nextLineEnd];
            if (line.Trim().Length == 0)
            {
                if (nextLineEnd < 0)
                {
                    return null;
                }

                cursor = nextLineEnd + 1;
                continue;
            }

            var match = ImageLinePattern.Match(line);
            if (match.Success)
            {
                return match.Groups["target"].Value.Trim('<', '>');
            }

            var inlineMarkdownMatch = MarkdownImagePattern.Match(line);
            if (inlineMarkdownMatch.Success)
            {
                return inlineMarkdownMatch.Groups["target"].Value.Trim('<', '>');
            }

            var htmlMatch = HtmlImagePattern.Match(line);
            if (htmlMatch.Success)
            {
                return htmlMatch.Groups["target"].Value;
            }

            var sourceMatch = HtmlSourcePattern.Match(line);
            return sourceMatch.Success ? sourceMatch.Groups["target"].Value : null;
        }

        return null;
    }

    private static string ApplyLink(string markdown, BatchLink link, string newline)
    {
        var htmlRewrite = RewriteAssociatedHtmlImage(markdown, link);
        if (htmlRewrite is not null)
        {
            return htmlRewrite;
        }

        var imageLine = $"![{link.Job.Alt}]({link.RelativeLink})";
        var markerLineEnd = markdown.IndexOf('\n', link.Job.MarkerEnd);
        if (markerLineEnd < 0)
        {
            return markdown + newline + imageLine + newline;
        }

        var cursor = markerLineEnd + 1;
        while (cursor <= markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', cursor);
            var line = lineEnd < 0 ? markdown[cursor..] : markdown[cursor..lineEnd];
            if (line.Trim().Length == 0)
            {
                if (lineEnd < 0)
                {
                    return markdown[..cursor] + imageLine + newline;
                }

                cursor = lineEnd + 1;
                continue;
            }

            var existingImage = ImageLinePattern.Match(line);
            if (existingImage.Success)
            {
                var target = existingImage.Groups["target"];
                var replacement = target.Value.StartsWith('<')
                    ? $"<{link.RelativeLink}>"
                    : link.RelativeLink;
                var updatedLine =
                    line[..target.Index] + replacement + line[(target.Index + target.Length)..];
                var after = lineEnd < 0 ? string.Empty : markdown[(lineEnd + 1)..];
                return markdown[..cursor]
                    + updatedLine
                    + (lineEnd < 0 ? string.Empty : newline + after);
            }

            return markdown[..(markerLineEnd + 1)]
                + imageLine
                + newline
                + markdown[(markerLineEnd + 1)..];
        }

        return markdown;
    }

    private static string? RewriteAssociatedHtmlImage(string markdown, BatchLink link)
    {
        var cursor = link.Job.MarkerEnd;
        while (cursor <= markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', cursor);
            var line = lineEnd < 0 ? markdown[cursor..] : markdown[cursor..lineEnd];
            if (line.Trim().Length == 0)
            {
                if (lineEnd < 0)
                {
                    return null;
                }

                cursor = lineEnd + 1;
                continue;
            }

            var match = HtmlImagePattern.Match(line);
            if (!match.Success)
            {
                match = HtmlSourcePattern.Match(line);
            }
            if (!match.Success)
            {
                match = MarkdownImagePattern.Match(line);
                if (!match.Success)
                {
                    return null;
                }
            }

            var target = match.Groups["target"];
            var updatedLine =
                line[..target.Index] + link.RelativeLink + line[(target.Index + target.Length)..];
            return markdown[..cursor]
                + updatedLine
                + (lineEnd < 0 ? string.Empty : markdown[lineEnd..]);
        }

        return null;
    }

    private static List<string>? Tokenize(string text, int line, List<BatchParseError> errors)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        char? quote = null;
        var hasToken = false;

        void Flush()
        {
            if (!hasToken)
            {
                return;
            }
            tokens.Add(builder.ToString());
            builder.Clear();
            hasToken = false;
        }

        foreach (var ch in text)
        {
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
            }
            else if (char.IsWhiteSpace(ch))
            {
                Flush();
            }
            else
            {
                builder.Append(ch);
                hasToken = true;
            }
        }

        if (quote.HasValue)
        {
            errors.Add(new BatchParseError(line, "unterminated quote in marker options."));
            return null;
        }

        Flush();
        return tokens;
    }

    private static bool IsShellLanguage(string language) => ShellLanguages.Contains(language);

    private static string FirstToken(string text)
    {
        var end = text.IndexOfAny([' ', '\t']);
        return (end < 0 ? text : text[..end]).Trim().ToLowerInvariant();
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }
            var single = trimmed
                .Replace("]", ")", StringComparison.Ordinal)
                .Replace("|", "/", StringComparison.Ordinal);
            return single.Length > 80 ? single[..80] : single;
        }

        return "console2svg";
    }

    private static bool HasErrorAtLine(IReadOnlyList<BatchParseError> errors, int line) =>
        errors.Any(error => error.Line == line);

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
