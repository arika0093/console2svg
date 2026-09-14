using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.QuickLeak;

/// <summary>Describes one secret-like match found in an input string.</summary>
/// <param name="RuleId">The Betterleaks or ConsoleToSvg rule identifier.</param>
/// <param name="Start">The zero-based UTF-16 start offset of the match.</param>
/// <param name="End">The exclusive zero-based UTF-16 end offset of the match.</param>
public readonly record struct QuickLeakFinding(string RuleId, int Start, int End);

/// <summary>Selects the generated pattern set used by <see cref="QuickLeak"/>.</summary>
public enum QuickLeakScanMode
{
    Normal,
    Early,
}

/// <summary>
/// Provides fast, source-generated secret detection without external runtime dependencies.
/// </summary>
public static partial class QuickLeak
{
    /// <summary>
    /// Finds all matches, sorts them by start and end offset, and materializes the results.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="mode">The normal or partial-input pattern set to use.</param>
    /// <returns>The sorted findings.</returns>
    public static IReadOnlyList<QuickLeakFinding> Scan(
        string text,
        QuickLeakScanMode mode = QuickLeakScanMode.Normal
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        var findings = EnumerateGeneratedRules(text, mode).ToArray();
        Array.Sort(
            findings,
            static (a, b) =>
                a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End)
        );
        return findings;
    }

    public static IEnumerable<QuickLeakFinding> Enumerate(
        string text,
        QuickLeakScanMode mode = QuickLeakScanMode.Normal
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        return EnumerateGeneratedRules(text, mode);
    }
}
/// <summary>
/// Enumerates matches directly from the generated rules without materializing an array.
/// </summary>
/// <param name="text">The text to scan.</param>
/// <param name="mode">The normal or partial-input pattern set to use.</param>
/// <returns>An iterator of findings in generated-rule order.</returns>
