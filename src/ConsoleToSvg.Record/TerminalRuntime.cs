using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Recording;

[JsonConverter(typeof(JsonStringEnumConverter<TerminalInputKind>))]
public enum TerminalInputKind
{
    Text,
    Key,
    Paste,
    RawHex,
}

public sealed record TerminalInput(TerminalInputKind Kind, string Value);

public interface ITerminalAction { }

public sealed record SendTerminalInputAction(TerminalInput[] Inputs) : ITerminalAction;

public sealed record ResizeTerminalAction(int Width, int Height) : ITerminalAction;

public sealed record CaptureTerminalAction(string OutputPath, SvgRenderOptions RenderOptions)
    : ITerminalAction;

public sealed record StopTerminalAction() : ITerminalAction;

public enum TerminalConditionUntil
{
    Present,
    Absent,
}

public sealed record TerminalCondition(
    string? Text = null,
    string? Regex = null,
    TerminalConditionUntil Until = TerminalConditionUntil.Present,
    TimeSpan? StableFor = null,
    TimeSpan? Timeout = null
);

public sealed record TerminalObservation(ManagedSessionSnapshot Session, DateTimeOffset ObservedAt);

public sealed record TerminalConditionResult(
    TerminalObservation Observation,
    bool Matched,
    bool TimedOut,
    string Result
);

public sealed class TerminalConditionEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private readonly Func<string, bool> _matches;
    private readonly TerminalConditionUntil _until;
    private bool _hasBeenPresent;

    public TerminalConditionEvaluator(TerminalCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (string.IsNullOrEmpty(condition.Text) == string.IsNullOrEmpty(condition.Regex))
        {
            throw new FormatException("Specify exactly one of terminal condition text or regex.");
        }

        if (
            condition.StableFor is TimeSpan stableFor
            && (stableFor <= TimeSpan.Zero || !double.IsFinite(stableFor.TotalMilliseconds))
        )
        {
            throw new FormatException("The stable-for duration must be positive.");
        }
        if (
            condition.Timeout is TimeSpan timeout
            && (timeout <= TimeSpan.Zero || !double.IsFinite(timeout.TotalMilliseconds))
        )
        {
            throw new FormatException("The timeout duration must be positive.");
        }

        _matches = CreateMatcher(condition);
        _until = condition.Until;
    }

    public bool IsSatisfied(string screenText)
    {
        var contains = _matches(screenText);
        if (_until == TerminalConditionUntil.Present)
        {
            return contains;
        }
        if (_until != TerminalConditionUntil.Absent)
        {
            throw new InvalidOperationException("The terminal condition target is unsupported.");
        }
        if (contains)
        {
            _hasBeenPresent = true;
            return false;
        }
        return _hasBeenPresent;
    }

    private static Func<string, bool> CreateMatcher(TerminalCondition condition)
    {
        if (condition.Regex is { } pattern)
        {
            try
            {
                var regex = new Regex(
                    pattern,
                    RegexOptions.CultureInvariant | RegexOptions.Multiline,
                    RegexTimeout
                );
                return text =>
                {
                    try
                    {
                        return regex.IsMatch(text);
                    }
                    catch (RegexMatchTimeoutException exception)
                    {
                        throw new FormatException(
                            "The terminal condition regex timed out.",
                            exception
                        );
                    }
                };
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("The terminal condition regex is invalid.", exception);
            }
        }

        var expected = condition.Text!;
        return screen => screen.Contains(expected, StringComparison.Ordinal);
    }
}

public static class TerminalSessionRuntime
{
    public static async Task<ManagedSessionResponse> ExecuteAsync(
        string sessionId,
        ITerminalAction action,
        CancellationToken cancellationToken = default,
        SessionJournalEntry? journalEntry = null
    )
    {
        ArgumentNullException.ThrowIfNull(action);

        var request = action switch
        {
            SendTerminalInputAction send when send.Inputs is { Length: > 0 } =>
                new ManagedSessionRequest
                {
                    Operation = "send",
                    Inputs = Array.ConvertAll(send.Inputs, ToManagedInput),
                },
            SendTerminalInputAction => throw new ArgumentException(
                "At least one terminal input is required.",
                nameof(action)
            ),
            ResizeTerminalAction resize => new ManagedSessionRequest
            {
                Operation = "resize",
                Width = resize.Width,
                Height = resize.Height,
            },
            CaptureTerminalAction capture => new ManagedSessionRequest
            {
                Operation = "capture",
                OutputPath = capture.OutputPath,
                RenderOptions = capture.RenderOptions,
            },
            StopTerminalAction => new ManagedSessionRequest { Operation = "stop" },
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        var response = await ManagedTerminalSessionManager
            .RequestAsync(sessionId, request, cancellationToken)
            .ConfigureAwait(false);
        await SessionJournalStore
            .AppendAsync(sessionId, journalEntry ?? CreateJournalEntry(action), cancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    public static async Task<TerminalObservation> ObserveAsync(
        string sessionId,
        bool includeStructuredScreen = false,
        string? observation = null,
        CancellationToken cancellationToken = default
    )
    {
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                sessionId,
                new ManagedSessionRequest
                {
                    Operation = "read",
                    IncludeStructuredScreen = includeStructuredScreen,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        if (observation is not null)
        {
            await SessionJournalStore
                .AppendAsync(
                    sessionId,
                    new SessionJournalEntry
                    {
                        Kind = "observation",
                        Name = observation,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return new TerminalObservation(response.Session, DateTimeOffset.UtcNow);
    }

    public static async Task<TerminalConditionResult> WaitAsync(
        string sessionId,
        TerminalCondition condition,
        CancellationToken cancellationToken = default,
        string? phase = null,
        bool recordCondition = true
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        var evaluator = new TerminalConditionEvaluator(condition);
        var stopwatch = Stopwatch.StartNew();
        long? conditionStartedAt = null;
        ManagedSessionSnapshot? snapshot = null;

        while (true)
        {
            if (snapshot is null)
            {
                snapshot = (
                    await ManagedTerminalSessionManager
                        .RequestAsync(
                            sessionId,
                            new ManagedSessionRequest { Operation = "read" },
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                ).Session;
            }

            var matched = evaluator.IsSatisfied(snapshot.Text);

            if (!matched)
            {
                conditionStartedAt = null;
            }
            else if (conditionStartedAt is null)
            {
                conditionStartedAt = Stopwatch.GetTimestamp();
            }

            var stableElapsed = conditionStartedAt is long startedAt
                ? Stopwatch.GetElapsedTime(startedAt)
                : TimeSpan.Zero;
            if (matched && stableElapsed >= (condition.StableFor ?? TimeSpan.Zero))
            {
                return await CompleteWaitAsync(
                        sessionId,
                        condition,
                        snapshot,
                        matched,
                        false,
                        cancellationToken,
                        phase,
                        recordCondition
                    )
                    .ConfigureAwait(false);
            }

            if (condition.Timeout is TimeSpan timeout && stopwatch.Elapsed >= timeout)
            {
                return await CompleteWaitAsync(
                        sessionId,
                        condition,
                        snapshot,
                        false,
                        true,
                        cancellationToken,
                        phase,
                        recordCondition
                    )
                    .ConfigureAwait(false);
            }

            if (snapshot.State != "running")
            {
                return await CompleteWaitAsync(
                        sessionId,
                        condition,
                        snapshot,
                        false,
                        false,
                        cancellationToken,
                        phase,
                        recordCondition
                    )
                    .ConfigureAwait(false);
            }

            var waitFor = TimeSpan.FromSeconds(1);
            if (matched && condition.StableFor is TimeSpan stableFor)
            {
                waitFor = Min(waitFor, stableFor - stableElapsed);
            }
            if (condition.Timeout is TimeSpan timeoutDuration)
            {
                waitFor = Min(waitFor, timeoutDuration - stopwatch.Elapsed);
            }

            var response = await ManagedTerminalSessionManager
                .RequestAsync(
                    sessionId,
                    new ManagedSessionRequest
                    {
                        Operation = "read",
                        WaitMs = Math.Max(1, (int)Math.Ceiling(waitFor.TotalMilliseconds)),
                        SinceVersion = snapshot.Version,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            snapshot = response.Session;
        }
    }

    private static async Task<TerminalConditionResult> CompleteWaitAsync(
        string sessionId,
        TerminalCondition condition,
        ManagedSessionSnapshot snapshot,
        bool matched,
        bool timedOut,
        CancellationToken cancellationToken,
        string? phase,
        bool recordCondition
    )
    {
        var result = (matched, timedOut) switch
        {
            (true, _) => "matched",
            (_, true) => "timeout",
            _ => "session-ended",
        };
        if (recordCondition)
        {
            await SessionJournalStore
                .AppendAsync(
                    sessionId,
                    new SessionJournalEntry
                    {
                        Kind = "condition",
                        Name = condition.Regex is null ? "text" : "regex",
                        Phase = phase,
                        Text = condition.Text,
                        Regex = condition.Regex,
                        Until = condition.Until.ToString().ToLowerInvariant(),
                        StableForMilliseconds = condition.StableFor?.TotalMilliseconds,
                        TimeoutMilliseconds = condition.Timeout?.TotalMilliseconds,
                        Matched = matched,
                        TimedOut = timedOut,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return new TerminalConditionResult(
            new TerminalObservation(snapshot, DateTimeOffset.UtcNow),
            matched,
            timedOut,
            result
        );
    }

    private static ManagedSessionInput ToManagedInput(TerminalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ManagedSessionInput
        {
            Type = input.Kind switch
            {
                TerminalInputKind.Text => "text",
                TerminalInputKind.Key => "key",
                TerminalInputKind.Paste => "paste",
                TerminalInputKind.RawHex => "raw",
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            },
            Value = input.Value,
        };
    }

    private static SessionJournalEntry CreateJournalEntry(ITerminalAction action) =>
        action switch
        {
            SendTerminalInputAction send => new SessionJournalEntry
            {
                Kind = "action",
                Name = "send",
                Inputs = send.Inputs,
                At = DateTimeOffset.UtcNow,
            },
            ResizeTerminalAction resize => new SessionJournalEntry
            {
                Kind = "action",
                Name = "resize",
                Width = resize.Width,
                Height = resize.Height,
                At = DateTimeOffset.UtcNow,
            },
            CaptureTerminalAction capture => new SessionJournalEntry
            {
                Kind = "action",
                Name = "capture",
                OutputPath = Path.GetFullPath(capture.OutputPath),
                At = DateTimeOffset.UtcNow,
            },
            StopTerminalAction => new SessionJournalEntry
            {
                Kind = "action",
                Name = "stop",
                At = DateTimeOffset.UtcNow,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    private static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first <= second ? first : second;
}
