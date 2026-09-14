using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.QuickLeaks;

/// <summary>Describes one secret-like match found in an input string.</summary>
/// <param name="RuleId">The Betterleaks or ConsoleToSvg rule identifier.</param>
/// <param name="Start">The zero-based UTF-16 start offset of the match.</param>
/// <param name="End">The exclusive zero-based UTF-16 end offset of the match.</param>
public readonly record struct QuickLeaksFinding(string RuleId, int Start, int End);

/// <summary>Selects the generated pattern set used by <see cref="QuickLeaks"/>.</summary>
public enum QuickLeaksScanMode
{
    Normal,
    Early,
}

/// <summary>
/// Provides fast, source-generated secret detection without external runtime dependencies.
/// </summary>
public static partial class QuickLeaks
{
    /// <summary>
    /// Finds all matches, sorts them by start and end offset, and materializes the results.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="mode">The normal or partial-input pattern set to use.</param>
    /// <returns>The sorted findings.</returns>
    public static IReadOnlyList<QuickLeaksFinding> Scan(
        string text,
        QuickLeaksScanMode mode = QuickLeaksScanMode.Normal
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        var findings = Enumerate(text, mode).ToArray();
        Array.Sort(
            findings,
            static (a, b) =>
                a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End)
        );
        return findings;
    }

    public static IEnumerable<QuickLeaksFinding> Enumerate(
        string text,
        QuickLeaksScanMode mode = QuickLeaksScanMode.Normal
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        return EnumerateNarrowed(text, mode);
    }

    private static IEnumerable<QuickLeaksFinding> EnumerateNarrowed(
        string text,
        QuickLeaksScanMode mode
    )
    {
        foreach (var finding in EnumerateGeneratedRules(text, mode))
        {
            var narrowed = NarrowFinding(text, finding);
            if (narrowed.End > narrowed.Start)
            {
                yield return narrowed;
            }
        }
    }

    /// <summary>
    /// Narrows a raw regex match to the sensitive value portion, preserving
    /// prefixes such as <c>C:\Users\</c> or <c>PASSWORD=</c> for readability.
    /// </summary>
    private static QuickLeaksFinding NarrowFinding(string text, QuickLeaksFinding finding)
    {
        var start = Math.Max(0, finding.Start);
        var end = Math.Min(text.Length, finding.End);
        if (end <= start)
        {
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        var span = text.AsSpan(start, end - start);
        if (span.IsEmpty)
        {
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        if (string.Equals(finding.RuleId, "console2svg-home-directory", StringComparison.Ordinal))
        {
            var lastSlash = span.LastIndexOf('/');
            var lastBackslash = span.LastIndexOf('\\');
            var lastSeparator = Math.Max(lastSlash, lastBackslash);
            if (lastSeparator >= 0 && lastSeparator + 1 < span.Length)
            {
                return new QuickLeaksFinding(finding.RuleId, start + lastSeparator + 1, end);
            }
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        if (
            string.Equals(finding.RuleId, "console2svg-credential-uri", StringComparison.Ordinal)
            || string.Equals(finding.RuleId, "generic-credential-uri", StringComparison.Ordinal)
        )
        {
            return NarrowCredentialUri(finding, start, span);
        }

        // Generic key=value secrets: mask only the value after '=' or ':'.
        // Token-only matches (no delimiter) keep the whole span.
        if (span.Contains("://".AsSpan(), StringComparison.Ordinal))
        {
            return finding;
        }

        var equalsIndex = span.IndexOf('=');
        var colonIndex = span.IndexOf(':');
        var delimiterIndex = equalsIndex >= 0 ? equalsIndex : colonIndex;
        if (delimiterIndex <= 0 || delimiterIndex >= span.Length - 1)
        {
            return finding;
        }

        var valueOffset = delimiterIndex + 1;
        while (valueOffset < span.Length && (span[valueOffset] == ' ' || span[valueOffset] == '\t'))
        {
            valueOffset++;
        }
        if (valueOffset >= span.Length)
        {
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        var quote = span[valueOffset];
        if (quote == '"' || quote == '\'' || quote == '`')
        {
            var valueStart = valueOffset + 1;
            var closingOffset = -1;
            for (var index = valueStart; index < span.Length; index++)
            {
                if (span[index] == quote && (index == valueStart || span[index - 1] != '\\'))
                {
                    closingOffset = index;
                    break;
                }
            }
            if (closingOffset >= 0)
            {
                if (closingOffset > valueStart)
                {
                    return new QuickLeaksFinding(
                        finding.RuleId,
                        start + valueStart,
                        start + closingOffset
                    );
                }
                return new QuickLeaksFinding(finding.RuleId, start, start);
            }
            // Unclosed quote (Early mode): mask to the end, trimming trailing whitespace.
            var trailingEnd = span.Length;
            while (
                trailingEnd > valueStart
                && (
                    span[trailingEnd - 1] == ' '
                    || span[trailingEnd - 1] == '\t'
                    || span[trailingEnd - 1] == '\r'
                    || span[trailingEnd - 1] == '\n'
                )
            )
            {
                trailingEnd--;
            }
            if (trailingEnd > valueStart)
            {
                return new QuickLeaksFinding(
                    finding.RuleId,
                    start + valueStart,
                    start + trailingEnd
                );
            }
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        var unquotedStart = valueOffset;
        // Skip stray delimiter characters from '=>', ':=', etc.
        while (
            unquotedStart < span.Length
            && (
                span[unquotedStart] == '='
                || span[unquotedStart] == ':'
                || span[unquotedStart] == '>'
            )
        )
        {
            unquotedStart++;
        }
        while (
            unquotedStart < span.Length
            && (span[unquotedStart] == ' ' || span[unquotedStart] == '\t')
        )
        {
            unquotedStart++;
        }
        if (unquotedStart >= span.Length)
        {
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }

        var unquotedEnd = unquotedStart;
        var isUsernameRule = string.Equals(
            finding.RuleId,
            "generic-username",
            StringComparison.Ordinal
        );
        while (unquotedEnd < span.Length)
        {
            var character = span[unquotedEnd];
            if (
                character == ' '
                || character == '\t'
                || character == '\r'
                || character == '\n'
                || character == '"'
                || character == '\''
                || character == '`'
                || character == ','
                || character == ';'
                || character == ')'
                || character == ']'
                || character == '}'
                || (
                    isUsernameRule
                    && (
                        character == '@' || character == '/' || character == '?' || character == '#'
                    )
                )
            )
            {
                break;
            }
            unquotedEnd++;
        }
        if (unquotedEnd > unquotedStart)
        {
            return new QuickLeaksFinding(
                finding.RuleId,
                start + unquotedStart,
                start + unquotedEnd
            );
        }
        return new QuickLeaksFinding(finding.RuleId, start, start);
    }

    private static QuickLeaksFinding NarrowCredentialUri(
        QuickLeaksFinding finding,
        int start,
        ReadOnlySpan<char> span
    )
    {
        var schemeIndex = span.IndexOf("://".AsSpan(), StringComparison.Ordinal);
        if (schemeIndex < 0)
        {
            return finding;
        }
        var credentialsStart = schemeIndex + 3;
        if (credentialsStart >= span.Length)
        {
            return finding;
        }
        var atIndex = span.LastIndexOf('@');
        var credentialsEnd = atIndex >= 0 ? atIndex : span.Length;
        if (credentialsEnd <= credentialsStart)
        {
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }
        var credentials = span[credentialsStart..credentialsEnd];
        var colonIndex = credentials.IndexOf(':');
        if (colonIndex >= 0)
        {
            var passwordStart = credentialsStart + colonIndex + 1;
            if (passwordStart < credentialsEnd)
            {
                return new QuickLeaksFinding(
                    finding.RuleId,
                    start + passwordStart,
                    start + credentialsEnd
                );
            }
            return new QuickLeaksFinding(finding.RuleId, start, start);
        }
        return new QuickLeaksFinding(
            finding.RuleId,
            start + credentialsStart,
            start + credentialsEnd
        );
    }
}
/// <summary>
/// Enumerates matches directly from the generated rules without materializing an array.
/// </summary>
/// <param name="text">The text to scan.</param>
/// <param name="mode">The normal or partial-input pattern set to use.</param>
/// <returns>An iterator of findings in generated-rule order.</returns>
