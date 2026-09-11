using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Configuration.Writable;

namespace ConsoleToSvg.Recording;

/// <summary>Canonical, YAML-backed replay session document.</summary>
[OptionsModel(Id = "console2svg.replay", Version = 2)]
public sealed partial class ReplayDocumentV2
{
    public SessionOptions Options { get; set; } = new();

    public string? Command { get; set; }

    public ReplayDefaults Defaults { get; set; } = new();

    /// <summary>Optional timeout for the complete replay session.</summary>
    public string? Timeout { get; set; }

    public List<ReplayStep> Steps { get; set; } = [];

    public string? AppVersion { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Migrates legacy timed input events without input normalization.</summary>
    public ReplayDocumentV2 Migrate(ReplayDocumentV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = new ReplayDocumentV2
        {
            AppVersion = source.AppVersion,
            CreatedAt = source.CreatedAt,
            Steps = ReplayMigration.MigrateEvents(source.Replay),
        };

        if (source.TotalDuration is double totalDuration && totalDuration >= 0d)
        {
            // v1 ended the entire replay after this duration plus a one-second
            // grace period. Retain that deadline separately from action defaults.
            document.Timeout = ReplayDuration.Format(totalDuration + 1d);
        }

        return document;
    }

    public ReplayDocumentV2 DeepClone() =>
        new()
        {
            Options = Options.DeepClone(),
            Command = Command,
            Defaults = Defaults.DeepClone(),
            Timeout = Timeout,
            Steps = Steps.Select(step => step.DeepClone()).ToList(),
            AppVersion = AppVersion,
            CreatedAt = CreatedAt,
        };
}

public sealed class ReplayDefaults
{
    /// <summary>Default timeout for actions that can wait.</summary>
    public string? Timeout { get; set; }

    internal ReplayDefaults DeepClone() => new() { Timeout = Timeout };
}

/// <summary>One reproducible interaction action.</summary>
public sealed class ReplayStep
{
    public ReplayWaitFor? WaitFor { get; set; }

    public string? Input { get; set; }

    public string? Key { get; set; }

    public List<string>? Modifiers { get; set; }

    public string? Sleep { get; set; }

    /// <summary>Lossless low-level input compatibility action.</summary>
    public string? Raw { get; set; }

    /// <summary>Per-action timeout, for example <c>10s</c>.</summary>
    public string? Timeout { get; set; }

    /// <summary>Delay between Unicode scalar values in an input action.</summary>
    public string? Interval { get; set; }

    internal ReplayStep DeepClone() =>
        new()
        {
            WaitFor = WaitFor?.DeepClone(),
            Input = Input,
            Key = Key,
            Modifiers = Modifiers is null ? null : [.. Modifiers],
            Sleep = Sleep,
            Raw = Raw,
            Timeout = Timeout,
            Interval = Interval,
        };
}

public sealed class ReplayWaitFor
{
    public string? Text { get; set; }

    public string? Regex { get; set; }

    /// <summary>Only match output that arrives after this action begins.</summary>
    public bool NewOutput { get; set; }

    /// <summary><c>visible</c> (default) or <c>scrollback</c>.</summary>
    public string Scope { get; set; } = "visible";

    internal ReplayWaitFor DeepClone() =>
        new()
        {
            Text = Text,
            Regex = Regex,
            NewOutput = NewOutput,
            Scope = Scope,
        };
}

internal static class ReplayMigration
{
    internal static List<ReplayStep> MigrateEvents(IReadOnlyList<InputEvent> events)
    {
        var steps = new List<ReplayStep>(events.Count * 2);
        var previousTime = 0d;
        foreach (var source in events)
        {
            var hasTiming = source.Time.HasValue || source.Tick.HasValue;
            var absoluteTime = source.Time ?? (previousTime + (source.Tick ?? 0d));
            if (hasTiming)
            {
                var delay = source.Time.HasValue ? absoluteTime - previousTime : source.Tick!.Value;
                if (delay < 0d || !double.IsFinite(delay))
                {
                    throw new FormatException(
                        "Legacy replay event timing must be finite and non-negative."
                    );
                }

                steps.Add(new ReplayStep { Sleep = ReplayDuration.Format(delay) });
                previousTime = absoluteTime;
            }

            if (string.Equals(source.Type, "raw", StringComparison.Ordinal))
            {
                steps.Add(new ReplayStep { Raw = source.Key });
            }
            else
            {
                steps.Add(
                    new ReplayStep
                    {
                        Key = source.Key,
                        Modifiers = source.Modifiers is { Length: > 0 }
                            ? [.. source.Modifiers]
                            : null,
                    }
                );
            }
        }

        return steps;
    }
}

internal static class ReplayDuration
{
    internal static TimeSpan Parse(string value, string fieldName)
    {
        if (!TryParse(value, out var result))
        {
            throw new FormatException(
                $"{fieldName} must be a non-negative duration such as '30ms', '10s', '2m', or '1h'."
            );
        }

        return result;
    }

    internal static bool TryParse(string? value, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var unitStart = 0;
        while (unitStart < text.Length && (char.IsDigit(text[unitStart]) || text[unitStart] == '.'))
        {
            unitStart++;
        }

        if (
            unitStart == 0
            || !double.TryParse(
                text.AsSpan(0, unitStart),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount
            )
            || !double.IsFinite(amount)
            || amount < 0d
        )
        {
            return false;
        }

        var multiplier = text[unitStart..].ToLowerInvariant() switch
        {
            "ms" => 0.001d,
            "s" => 1d,
            "m" => 60d,
            "h" => 3600d,
            _ => -1d,
        };
        if (multiplier < 0d || amount > TimeSpan.MaxValue.TotalSeconds / multiplier)
        {
            return false;
        }

        result = TimeSpan.FromSeconds(amount * multiplier);
        return true;
    }

    internal static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        if (seconds == 0d)
        {
            return "0s";
        }

        var milliseconds = seconds * 1000d;
        if (Math.Abs(milliseconds - Math.Round(milliseconds)) < 0.0000001d)
        {
            return $"{Math.Round(milliseconds).ToString(CultureInfo.InvariantCulture)}ms";
        }

        return $"{seconds.ToString("0.#######", CultureInfo.InvariantCulture)}s";
    }
}
