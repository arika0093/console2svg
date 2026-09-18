using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Batch;

/// <summary>
/// Locates Markdown regions where marker-looking text is illustrative code rather than
/// executable document content. The scanner is intentionally conservative: ambiguous
/// code-shaped regions are excluded from batch marker execution.
/// </summary>
internal static class MarkdownCodeContext
{
    internal readonly record struct TextRange(int Start, int End)
    {
        public bool Contains(int index) => index >= Start && index < End;
    }

    private sealed record FenceState(char Character, int Length, int QuoteDepth, int Start);

    public static IReadOnlyList<TextRange> FindExcludedRanges(string markdown)
    {
        var ranges = new List<TextRange>();
        AddBlockCodeRanges(markdown, ranges);
        ranges = Merge(ranges);
        AddHtmlCodeRanges(markdown, ranges);
        ranges = Merge(ranges);
        AddInlineCodeRanges(markdown, ranges);
        return Merge(ranges);
    }

    private static void AddBlockCodeRanges(string markdown, List<TextRange> ranges)
    {
        var offset = 0;
        FenceState? openFence = null;

        while (offset < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', offset);
            var next = lineEnd < 0 ? markdown.Length : lineEnd + 1;
            var rawEnd = lineEnd < 0 ? markdown.Length : lineEnd;
            if (rawEnd > offset && markdown[rawEnd - 1] == '\r')
            {
                rawEnd--;
            }

            var line = markdown[offset..rawEnd];
            var (containerStart, quoteDepth) = GetContainerStart(line);

            if (openFence is not null)
            {
                if (
                    quoteDepth == openFence.QuoteDepth
                    && IsFenceClose(line, containerStart, openFence.Character, openFence.Length)
                )
                {
                    ranges.Add(new TextRange(openFence.Start, next));
                    openFence = null;
                }

                offset = next;
                continue;
            }

            if (TryGetFenceOpen(line, containerStart, out var character, out var length))
            {
                openFence = new FenceState(character, length, quoteDepth, offset);
                offset = next;
                continue;
            }

            if (IsIndentedCode(line, containerStart))
            {
                ranges.Add(new TextRange(offset, next));
            }

            offset = next;
        }

        if (openFence is not null)
        {
            ranges.Add(new TextRange(openFence.Start, markdown.Length));
        }
    }

    private static (int ContentStart, int QuoteDepth) GetContainerStart(string line)
    {
        var index = 0;
        var quoteDepth = 0;

        while (index < line.Length)
        {
            var beforeIndent = index;
            var spaces = 0;
            while (spaces < 3 && index < line.Length && line[index] == ' ')
            {
                spaces++;
                index++;
            }

            if (index >= line.Length || line[index] != '>')
            {
                index = beforeIndent;
                break;
            }

            index++;
            quoteDepth++;
            if (index < line.Length && line[index] == ' ')
            {
                index++;
            }
        }

        return (index, quoteDepth);
    }

    private static bool TryGetFenceOpen(
        string line,
        int containerStart,
        out char character,
        out int length
    )
    {
        character = default;
        length = 0;
        var index = SkipUpToThreeSpaces(line, containerStart);
        if (index >= line.Length || line[index] is not ('`' or '~'))
        {
            return false;
        }

        character = line[index];
        var start = index;
        while (index < line.Length && line[index] == character)
        {
            index++;
        }

        length = index - start;
        if (length < 3)
        {
            return false;
        }

        if (character == '`' && line.AsSpan(index).Contains('`'))
        {
            return false;
        }

        return true;
    }

    private static bool IsFenceClose(
        string line,
        int containerStart,
        char character,
        int minimumLength
    )
    {
        var index = SkipUpToThreeSpaces(line, containerStart);
        var start = index;
        while (index < line.Length && line[index] == character)
        {
            index++;
        }

        return index - start >= minimumLength && string.IsNullOrWhiteSpace(line[index..]);
    }

    private static int SkipUpToThreeSpaces(string line, int index)
    {
        var spaces = 0;
        while (spaces < 3 && index < line.Length && line[index] == ' ')
        {
            spaces++;
            index++;
        }
        return index;
    }

    private static bool IsIndentedCode(string line, int containerStart)
    {
        var columns = 0;
        for (var index = containerStart; index < line.Length; index++)
        {
            if (line[index] == ' ')
            {
                columns++;
            }
            else if (line[index] == '\t')
            {
                columns += 4;
            }
            else
            {
                break;
            }

            if (columns >= 4)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddHtmlCodeRanges(string markdown, List<TextRange> ranges)
    {
        var index = 0;
        while (index < markdown.Length)
        {
            if (TryFindContainingRange(ranges, index, out var containing))
            {
                index = Math.Max(index + 1, containing.End);
                continue;
            }

            if (markdown[index] != '<' || !TryGetHtmlCodeTag(markdown, index, out var tag))
            {
                index++;
                continue;
            }

            var openingEnd = markdown.IndexOf('>', index + 1);
            if (openingEnd < 0)
            {
                ranges.Add(new TextRange(index, markdown.Length));
                break;
            }

            var closeStart = markdown.IndexOf(
                $"</{tag}",
                openingEnd + 1,
                StringComparison.OrdinalIgnoreCase
            );
            if (closeStart < 0)
            {
                ranges.Add(new TextRange(index, markdown.Length));
                break;
            }

            var closeEnd = markdown.IndexOf('>', closeStart + tag.Length + 2);
            var end = closeEnd < 0 ? markdown.Length : closeEnd + 1;
            ranges.Add(new TextRange(index, end));
            index = end;
        }
    }

    private static bool TryGetHtmlCodeTag(string markdown, int index, out string tag)
    {
        foreach (var candidate in new[] { "pre", "code" })
        {
            var nameStart = index + 1;
            if (
                nameStart + candidate.Length <= markdown.Length
                && markdown.AsSpan(nameStart, candidate.Length).Equals(
                    candidate.AsSpan(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                var boundary = nameStart + candidate.Length;
                if (
                    boundary == markdown.Length
                    || markdown[boundary] == '>'
                    || char.IsWhiteSpace(markdown[boundary])
                )
                {
                    tag = candidate;
                    return true;
                }
            }
        }

        tag = string.Empty;
        return false;
    }

    private static void AddInlineCodeRanges(string markdown, List<TextRange> ranges)
    {
        var index = 0;
        while (index < markdown.Length)
        {
            if (TryFindContainingRange(ranges, index, out var containing))
            {
                index = Math.Max(index + 1, containing.End);
                continue;
            }

            if (markdown[index] != '`')
            {
                index++;
                continue;
            }

            var openingStart = index;
            while (index < markdown.Length && markdown[index] == '`')
            {
                index++;
            }
            var runLength = index - openingStart;
            var closing = FindMatchingBacktickRun(markdown, index, runLength, ranges);
            if (closing < 0)
            {
                continue;
            }

            var end = closing + runLength;
            ranges.Add(new TextRange(openingStart, end));
            index = end;
        }
    }

    private static int FindMatchingBacktickRun(
        string markdown,
        int start,
        int runLength,
        IReadOnlyList<TextRange> ranges
    )
    {
        var index = start;
        while (index < markdown.Length)
        {
            if (TryFindContainingRange(ranges, index, out var containing))
            {
                index = Math.Max(index + 1, containing.End);
                continue;
            }

            if (markdown[index] != '`')
            {
                index++;
                continue;
            }

            var runStart = index;
            while (index < markdown.Length && markdown[index] == '`')
            {
                index++;
            }

            if (index - runStart == runLength)
            {
                return runStart;
            }
        }

        return -1;
    }

    private static bool TryFindContainingRange(
        IReadOnlyList<TextRange> ranges,
        int index,
        out TextRange containing
    )
    {
        foreach (var range in ranges)
        {
            if (range.Contains(index))
            {
                containing = range;
                return true;
            }
            if (range.Start > index)
            {
                break;
            }
        }

        containing = default;
        return false;
    }

    private static List<TextRange> Merge(IEnumerable<TextRange> ranges)
    {
        var ordered = ranges.OrderBy(range => range.Start).ThenBy(range => range.End).ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var merged = new List<TextRange> { ordered[0] };
        foreach (var range in ordered.Skip(1))
        {
            var previous = merged[^1];
            if (range.Start <= previous.End)
            {
                merged[^1] = new TextRange(previous.Start, Math.Max(previous.End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
