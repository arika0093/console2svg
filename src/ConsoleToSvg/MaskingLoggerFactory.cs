using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using ConsoleToSvg.QuickLeaks;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg;

internal sealed class MaskingLoggerFactory(
    ILoggerFactory inner,
    bool maskAuto,
    IReadOnlyList<string>? maskPatterns
) : ILoggerFactory
{
    private readonly QuickLeaksScanner _scanner = new(
        new QuickLeaksScannerOptions { SensitiveLiterals = CreateLiterals(maskPatterns) }
    );
    private readonly string[] _maskPatterns = maskPatterns is null ? [] : [.. maskPatterns];

    public ILogger CreateLogger(string categoryName) =>
        new MaskingLogger(inner.CreateLogger(categoryName), _scanner, maskAuto, _maskPatterns);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private static IReadOnlyList<QuickLeaksCustomLiteral> CreateLiterals(
        IReadOnlyList<string>? patterns
    )
    {
        if (patterns is null)
            return [];
        var literals = new List<QuickLeaksCustomLiteral>(patterns.Count);
        for (var index = 0; index < patterns.Count; index++)
        {
            if (!string.IsNullOrEmpty(patterns[index]))
                literals.Add(new($"mask-{index}", patterns[index]));
        }
        return literals;
    }
}

internal sealed class MaskingLogger(
    ILogger inner,
    QuickLeaksScanner scanner,
    bool maskAuto,
    IReadOnlyList<string> maskPatterns
) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += Environment.NewLine + exception;
        var masked = Mask(message, scanner, maskAuto, maskPatterns);
        inner.Log(logLevel, eventId, masked, null, static (value, _) => value);
    }

    private static string Mask(
        string value,
        QuickLeaksScanner scanner,
        bool maskAuto,
        IReadOnlyList<string> maskPatterns
    )
    {
        var ranges = new List<(int Start, int End)>();
        if (maskAuto)
        {
            var findings = new ArrayBufferWriter<QuickLeaksFinding>();
            scanner.Scan(value, findings);
            foreach (var finding in findings.WrittenSpan)
                ranges.Add((finding.Start, finding.End));
        }

        if (ranges.Count == 0)
            return ApplyPatterns(value, maskPatterns);

        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var masked = new System.Text.StringBuilder(value);
        for (var index = ranges.Count - 1; index >= 0; index--)
        {
            var (start, end) = ranges[index];
            masked.Remove(start, end - start).Insert(start, new string('*', end - start));
        }
        return ApplyPatterns(masked.ToString(), maskPatterns);
    }

    private static string ApplyPatterns(string value, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns.Where(pattern => !string.IsNullOrEmpty(pattern)))
        {
            value = value.Replace(
                pattern,
                new string('*', pattern.Length),
                StringComparison.Ordinal
            );
        }
        return value;
    }
}
