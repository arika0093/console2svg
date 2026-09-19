using System;
using System.Buffers;
using System.Collections.Generic;

namespace ConsoleToSvg.QuickLeaks;

public readonly record struct QuickLeaksCustomLiteral(
    string RuleId,
    string Value,
    StringComparison Comparison = StringComparison.Ordinal
);

public sealed class QuickLeaksScannerOptions
{
    public IReadOnlyList<QuickLeaksCustomLiteral> SensitiveLiterals { get; init; } =
        Array.Empty<QuickLeaksCustomLiteral>();
}

/// <summary>Combines the built-in generated scanner with construction-time custom literals.</summary>
public sealed class QuickLeaksScanner
{
    private readonly QuickLeaksCustomLiteral[] _ordinal;
    private readonly QuickLeaksCustomLiteral[] _ordinalIgnoreCase;
    private readonly SearchValues<string>? _ordinalSearch;
    private readonly SearchValues<string>? _ordinalIgnoreCaseSearch;

    public QuickLeaksScanner(QuickLeaksScannerOptions? options = null)
    {
        options ??= new QuickLeaksScannerOptions();
        var ordinal = new List<QuickLeaksCustomLiteral>();
        var ordinalIgnoreCase = new List<QuickLeaksCustomLiteral>();
        foreach (var literal in options.SensitiveLiterals)
        {
            ArgumentException.ThrowIfNullOrEmpty(literal.RuleId);
            ArgumentException.ThrowIfNullOrEmpty(literal.Value);
            switch (literal.Comparison)
            {
                case StringComparison.Ordinal:
                    ordinal.Add(literal);
                    break;
                case StringComparison.OrdinalIgnoreCase:
                    ordinalIgnoreCase.Add(literal);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        "Custom literals support only Ordinal and OrdinalIgnoreCase."
                    );
            }
        }
        _ordinal = ordinal.ToArray();
        _ordinalIgnoreCase = ordinalIgnoreCase.ToArray();
        _ordinalSearch = CreateSearchValues(_ordinal, StringComparison.Ordinal);
        _ordinalIgnoreCaseSearch = CreateSearchValues(
            _ordinalIgnoreCase,
            StringComparison.OrdinalIgnoreCase
        );
    }

    public int Scan(
        ReadOnlySpan<char> text,
        IBufferWriter<QuickLeaksFinding> destination,
        QuickLeaksScanMode mode = QuickLeaksScanMode.Normal
    )
    {
        var count = QuickLeaks.Scan(text, destination, mode);
        count += ScanCustom(text, destination, _ordinal, _ordinalSearch);
        count += ScanCustom(text, destination, _ordinalIgnoreCase, _ordinalIgnoreCaseSearch);
        return count;
    }

    private static SearchValues<string>? CreateSearchValues(
        QuickLeaksCustomLiteral[] literals,
        StringComparison comparison
    )
    {
        if (literals.Length == 0)
        {
            return null;
        }
        var values = new string[literals.Length];
        for (var index = 0; index < literals.Length; index++)
        {
            values[index] = literals[index].Value;
        }
        return SearchValues.Create(values, comparison);
    }

    private static int ScanCustom(
        ReadOnlySpan<char> text,
        IBufferWriter<QuickLeaksFinding> destination,
        QuickLeaksCustomLiteral[] literals,
        SearchValues<string>? searchValues
    )
    {
        if (searchValues is null)
        {
            return 0;
        }
        var count = 0;
        var offset = 0;
        while (offset < text.Length)
        {
            var relative = text[offset..].IndexOfAny(searchValues);
            if (relative < 0)
            {
                break;
            }
            var start = offset + relative;
            foreach (var literal in literals)
            {
                if (text[start..].StartsWith(literal.Value, literal.Comparison))
                {
                    var destinationSpan = destination.GetSpan(1);
                    destinationSpan[0] = new QuickLeaksFinding(
                        literal.RuleId,
                        start,
                        start + literal.Value.Length
                    );
                    destination.Advance(1);
                    count++;
                }
            }
            offset = start + 1;
        }
        return count;
    }
}
