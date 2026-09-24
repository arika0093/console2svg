using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg;

internal sealed class SessionStartOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

internal sealed class SessionReadOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int? ExitCode { get; init; }
    public long Version { get; init; }
    public CaptureJsonScreen Screen { get; init; } = new();
}

internal sealed class SessionWaitOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string Status { get; init; } = "completed";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionErrorDetail? Error { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int? ExitCode { get; init; }
    public long Version { get; init; }
    public string Result { get; init; } = string.Empty;
    public string Until { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool Matched { get; init; }
    public bool TimedOut { get; init; }
    public CaptureJsonScreen Screen { get; init; } = new();
}

internal sealed class SessionErrorDetail
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

internal sealed class SessionErrorOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string Status { get; init; } = "error";
    public SessionErrorDetail Error { get; init; } = new();
}

internal sealed class SessionTextCondition
{
    private readonly string _text;
    private readonly bool _untilAbsent;
    private bool _hasBeenPresent;

    public SessionTextCondition(string text, bool untilAbsent)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("The screen text must not be empty.", nameof(text));
        }

        _text = text;
        _untilAbsent = untilAbsent;
    }

    public bool IsSatisfied(string screenText)
    {
        var containsText = screenText.Contains(_text, StringComparison.Ordinal);
        if (!_untilAbsent)
        {
            return containsText;
        }

        if (containsText)
        {
            _hasBeenPresent = true;
            return false;
        }

        return _hasBeenPresent;
    }
}

internal sealed class SessionSummaryOutput
{
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int? ExitCode { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }
}

internal sealed class SessionListOutput
{
    public int SchemaVersion { get; init; } = 1;
    public SessionSummaryOutput[] Sessions { get; init; } = [];
}

internal sealed class SessionOperationOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
}

internal sealed class SessionCaptureOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public CaptureJsonArtifact Artifact { get; init; } = new();
}

internal sealed class SessionStopAllOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string[] Stopped { get; init; } = [];
    public SessionOperationFailure[] Failed { get; init; } = [];
}

internal sealed class SessionOperationFailure
{
    public string SessionId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionStartOutput))]
[JsonSerializable(typeof(SessionReadOutput))]
[JsonSerializable(typeof(SessionWaitOutput))]
[JsonSerializable(typeof(SessionListOutput))]
[JsonSerializable(typeof(SessionOperationOutput))]
[JsonSerializable(typeof(SessionCaptureOutput))]
[JsonSerializable(typeof(SessionStopAllOutput))]
[JsonSerializable(typeof(SessionErrorOutput))]
internal sealed partial class SessionOutputJsonContext : JsonSerializerContext { }

internal static partial class Program
{
    private static async Task<int> RunSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        if (options.RequestedSessionAction is SessionAction.Host)
        {
            var sessionDirectory =
                options.SessionDirectory
                ?? throw new InvalidOperationException("The session host directory is missing.");
            await using var hostLogStream = new FileStream(
                Path.Combine(sessionDirectory, "host.log"),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite
            );
            using var hostLog = new StreamWriter(
                hostLogStream,
                new UTF8Encoding(false),
                leaveOpen: true
            )
            {
                AutoFlush = true,
            };
            Console.SetOut(TextWriter.Null);
            Console.SetError(hostLog);
            await ManagedTerminalSessionHost
                .RunAsync(
                    options.SessionPipeName
                        ?? throw new InvalidOperationException(
                            "The session host pipe name is missing."
                        ),
                    sessionDirectory,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return 0;
        }

        try
        {
            return options.RequestedSessionAction switch
            {
                SessionAction.Start => await StartManagedSessionAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                SessionAction.List => WriteManagedSessionList(options.SessionListAll),
                SessionAction.Read => await ReadManagedSessionAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                SessionAction.Wait => await WaitManagedSessionAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                SessionAction.Send => await SendManagedSessionInputAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                SessionAction.Resize => await ResizeManagedSessionAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                SessionAction.Capture => await CaptureManagedSessionAsync(
                        options,
                        cancellationToken
                    )
                    .ConfigureAwait(false),
                SessionAction.Stop => await StopManagedSessionAsync(options, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new InvalidOperationException("A session action is required."),
            };
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or TimeoutException
                        or FormatException
                        or JsonException
            )
        {
            var code = GetSessionErrorCode(exception, options.RequestedSessionAction);
            await WriteSessionErrorAsync(code, exception.Message, cancellationToken)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> StartManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var command = options.SessionCommand ?? [];
        if (command.Length == 0)
        {
            throw new InvalidOperationException("A command is required after --.");
        }
        if (options.SessionWidth is < 1 or > 500 || options.SessionHeight is < 1 or > 500)
        {
            throw new InvalidOperationException("Terminal dimensions must be between 1 and 500.");
        }
        var workingDirectory = Path.GetFullPath(
            options.SessionWorkingDirectory ?? Environment.CurrentDirectory
        );
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException(
                $"Working directory does not exist: {workingDirectory}"
            );
        }

        var response = await ManagedTerminalSessionManager
            .StartAsync(
                command,
                options.SessionWidth,
                options.SessionHeight,
                workingDirectory,
                options.NoDeleteEnvs,
                cancellationToken
            )
            .ConfigureAwait(false);
        var session = response.Session;
        await WriteSessionJsonAsync(
                new SessionStartOutput
                {
                    SessionId = session.Id,
                    State = session.State,
                    ProcessId = session.ProcessId,
                    Width = session.Width,
                    Height = session.Height,
                },
                SessionOutputJsonContext.Default.SessionStartOutput,
                cancellationToken
            )
            .ConfigureAwait(false);
        return 0;
    }

    private static int WriteManagedSessionList(bool includeRetained)
    {
        var sessions = ManagedTerminalSessionManager.List();
        var output = new SessionListOutput
        {
            Sessions = sessions
                .Where(session =>
                    includeRetained || session.State is "starting" or "running"
                )
                .Select(session => new SessionSummaryOutput
                {
                    SessionId = session.Id,
                    State = session.State,
                    ProcessId = session.ProcessId,
                    ExitCode = session.ExitCode,
                    Width = session.Width,
                    Height = session.Height,
                    UpdatedAt = session.UpdatedAt,
                    ExpiresAt = session.ExpiresAt,
                })
                .ToArray(),
        };
        WriteSessionJson(output, SessionOutputJsonContext.Default.SessionListOutput);
        return 0;
    }

    private static async Task<int> ReadManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest
                {
                    Operation = "read",
                    IncludeStructuredScreen = options.SessionStructured,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        var session = response.Session;
        await WriteSessionJsonAsync(
                new SessionReadOutput
                {
                    SessionId = session.Id,
                    State = session.State,
                    ProcessId = session.ProcessId,
                    ExitCode = session.ExitCode,
                    Version = session.Version,
                    Screen = new CaptureJsonScreen
                    {
                        Width = session.Width,
                        Height = session.Height,
                        Text = session.Text,
                        Truncated = session.TextTruncated,
                        CursorRow = session.CursorRow,
                        CursorColumn = session.CursorColumn,
                        CursorVisible = session.CursorVisible,
                        IsAlternateScreen = session.IsAlternateScreen,
                        ScrollbackRows = session.ScrollbackRows,
                        Scope = "viewport",
                        Structured = options.SessionStructured ? session.Screen : null,
                    },
                },
                SessionOutputJsonContext.Default.SessionReadOutput,
                cancellationToken
            )
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> WaitManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var text =
            options.SessionWaitText
            ?? throw new InvalidOperationException("Screen text is required.");
        var until = options.SessionWaitUntil ?? "present";
        var untilAbsent = until.Equals("absent", StringComparison.OrdinalIgnoreCase);
        var stableFor = ParseOptionalSessionDuration(options.SessionWaitStableFor, "--stable-for");
        var timeout = ParseOptionalSessionDuration(options.SessionWaitTimeout, "--timeout");
        var condition = new SessionTextCondition(text, untilAbsent);
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
                            RequireSessionId(options),
                            new ManagedSessionRequest { Operation = "read" },
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                ).Session;
            }

            var matched = condition.IsSatisfied(snapshot.Text);
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
            if (matched && stableElapsed >= (stableFor ?? TimeSpan.Zero))
            {
                return WriteSessionWait(snapshot, text, until, "matched", matched, timedOut: false);
            }

            if (timeout is TimeSpan timeoutValue && stopwatch.Elapsed >= timeoutValue)
            {
                return WriteSessionWait(
                    snapshot,
                    text,
                    until,
                    "timeout",
                    matched: false,
                    timedOut: true
                );
            }

            if (snapshot.State != "running")
            {
                return WriteSessionWait(
                    snapshot,
                    text,
                    until,
                    "session-ended",
                    matched: false,
                    timedOut: false
                );
            }

            var waitFor = TimeSpan.FromSeconds(1);
            if (matched && stableFor is TimeSpan stableDuration)
            {
                waitFor = Min(waitFor, stableDuration - stableElapsed);
            }
            if (timeout is TimeSpan timeoutDuration)
            {
                waitFor = Min(waitFor, timeoutDuration - stopwatch.Elapsed);
            }

            var waitMs = Math.Max(1, (int)Math.Ceiling(waitFor.TotalMilliseconds));
            var response = await ManagedTerminalSessionManager
                .RequestAsync(
                    RequireSessionId(options),
                    new ManagedSessionRequest
                    {
                        Operation = "read",
                        WaitMs = waitMs,
                        SinceVersion = snapshot.Version,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            snapshot = response.Session;
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first <= second ? first : second;

    private static int WriteSessionWait(
        ManagedSessionSnapshot session,
        string text,
        string until,
        string result,
        bool matched,
        bool timedOut
    )
    {
        WriteSessionJson(
            new SessionWaitOutput
            {
                Status = matched ? "completed" : "error",
                Error = matched
                    ? null
                    : new SessionErrorDetail
                    {
                        Code = timedOut
                            ? "wait_timeout"
                            : session.State == "unavailable"
                                ? "host_unavailable"
                                : "session_exited",
                        Message = timedOut
                            ? $"Timed out waiting for text '{text}'."
                            : $"Session '{session.Id}' ended before the condition matched.",
                    },
                SessionId = session.Id,
                State = session.State,
                ProcessId = session.ProcessId,
                ExitCode = session.ExitCode,
                Version = session.Version,
                Result = result,
                Until = until,
                Text = text,
                Matched = matched,
                TimedOut = timedOut,
                Screen = new CaptureJsonScreen
                {
                    Width = session.Width,
                    Height = session.Height,
                    Text = session.Text,
                    Truncated = session.TextTruncated,
                    CursorRow = session.CursorRow,
                    CursorColumn = session.CursorColumn,
                    CursorVisible = session.CursorVisible,
                    IsAlternateScreen = session.IsAlternateScreen,
                    ScrollbackRows = session.ScrollbackRows,
                    Scope = "viewport",
                },
            },
            SessionOutputJsonContext.Default.SessionWaitOutput
        );
        if (!matched)
        {
            Console.Error.WriteLine(
                timedOut
                    ? $"Timed out waiting for text '{text}'."
                    : $"Session '{session.Id}' ended before the condition matched."
            );
        }
        return matched ? 0 : 1;
    }

    private static async Task<int> SendManagedSessionInputAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        if (options.SessionInputs.Count == 0)
        {
            throw new InvalidOperationException("At least one session input is required.");
        }
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest
                {
                    Operation = "send",
                    Inputs = options
                        .SessionInputs.Select(input =>
                            new ManagedSessionInput
                            {
                                Type = input.IsText ? "text" : input.IsRaw ? "raw" : "key",
                                Value = input.Value,
                            }
                        )
                        .ToArray(),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return WriteSessionOperation(response.Session);
    }

    private static async Task<int> ResizeManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest
                {
                    Operation = "resize",
                    Width = options.SessionWidth,
                    Height = options.SessionHeight,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return WriteSessionOperation(response.Session);
    }

    private static async Task<int> CaptureManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var outputFormat =
            options.Format
            ?? Path.GetExtension(options.OutputPath).TrimStart('.').ToLowerInvariant();
        if (
            !string.IsNullOrEmpty(outputFormat)
            && !outputFormat.Equals("svg", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "session capture currently supports SVG output only."
            );
        }

        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest
                {
                    Operation = "capture",
                    OutputPath = options.OutputPath,
                    RenderOptions = SvgRenderOptionsFactory.Create(options),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        await WriteSessionJsonAsync(
                new SessionCaptureOutput
                {
                    SessionId = response.Session.Id,
                    State = response.Session.State,
                    Artifact = new CaptureJsonArtifact
                    {
                        Path = Path.GetFullPath(options.OutputPath),
                        Format = "svg",
                    },
                },
                SessionOutputJsonContext.Default.SessionCaptureOutput,
                cancellationToken
            )
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> StopManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        if (!options.SessionAll)
        {
            var response = await ManagedTerminalSessionManager
                .RequestAsync(
                    RequireSessionId(options),
                    new ManagedSessionRequest { Operation = "stop" },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return WriteSessionOperation(response.Session);
        }

        var active = ManagedTerminalSessionManager
            .List()
            .Where(session => session.State is "running" or "starting")
            .ToArray();
        if (active.Length == 0)
        {
            await WriteSessionJsonAsync(
                    new SessionStopAllOutput(),
                    SessionOutputJsonContext.Default.SessionStopAllOutput,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return 0;
        }

        if (!options.SessionYes)
        {
            if (Console.IsInputRedirected)
            {
                throw new InvalidOperationException(
                    "session stop --all requires --yes when standard input is redirected."
                );
            }

            await Console
                .Error.WriteAsync(
                    $"Stop {active.Length} managed session(s)? [y/N] ".AsMemory(),
                    cancellationToken
                )
                .ConfigureAwait(false);
            var confirmation = await Console
                .In.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSessionErrorAsync(
                        "cancelled",
                        "Session stop was cancelled.",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return 1;
            }
        }

        var stopped = new System.Collections.Generic.List<string>();
        var failed = new System.Collections.Generic.List<SessionOperationFailure>();
        foreach (var sessionId in active.Select(session => session.Id))
        {
            try
            {
                await ManagedTerminalSessionManager
                    .RequestAsync(
                        sessionId,
                        new ManagedSessionRequest { Operation = "stop" },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                stopped.Add(sessionId);
            }
            catch (Exception exception)
                when (exception
                        is InvalidOperationException
                            or IOException
                            or TimeoutException
                            or UnauthorizedAccessException
                )
            {
                Console.Error.WriteLine($"{sessionId}: {exception.Message}");
                failed.Add(
                    new SessionOperationFailure
                    {
                        SessionId = sessionId,
                        Code = GetSessionErrorCode(exception, SessionAction.Stop),
                        Message = exception.Message,
                    }
                );
            }
        }

        await WriteSessionJsonAsync(
                new SessionStopAllOutput { Stopped = stopped.ToArray(), Failed = failed.ToArray() },
                SessionOutputJsonContext.Default.SessionStopAllOutput,
                cancellationToken
            )
            .ConfigureAwait(false);

        return failed.Count == 0 ? 0 : 1;
    }

    private static int WriteSessionOperation(ManagedSessionSnapshot session)
    {
        WriteSessionJson(
            new SessionOperationOutput
            {
                SessionId = session.Id,
                State = session.State,
                ExitCode = session.ExitCode,
            },
            SessionOutputJsonContext.Default.SessionOperationOutput
        );
        return 0;
    }

    private static string RequireSessionId(AppOptions options) =>
        options.SessionId
        ?? throw new InvalidOperationException("A managed session ID is required.");

    private static string GetSessionErrorCode(Exception exception, SessionAction? action) =>
        exception switch
        {
            ManagedSessionException sessionException => sessionException.Code,
            FormatException or ArgumentException when action == SessionAction.Wait =>
                "invalid_condition",
            InvalidDataException => "invalid_request",
            IOException or TimeoutException when action == SessionAction.Capture => "io_error",
            IOException or TimeoutException => "host_unavailable",
            OperationCanceledException => "cancelled",
            UnauthorizedAccessException => "permission_denied",
            ArgumentException or FormatException => "invalid_request",
            InvalidOperationException => "invalid_request",
            _ => "session_error",
        };

    private static async Task WriteSessionErrorAsync(
        string code,
        string message,
        CancellationToken cancellationToken
    )
    {
        await WriteSessionJsonAsync(
                new SessionErrorOutput
                {
                    Error = new SessionErrorDetail { Code = code, Message = message },
                },
                SessionOutputJsonContext.Default.SessionErrorOutput,
                cancellationToken
            )
            .ConfigureAwait(false);
        await Console.Error.WriteLineAsync(message.AsMemory(), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static TimeSpan? ParseOptionalSessionDuration(string? value, string optionName)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        var multiplier = 1000d;
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1d;
            text = text[..^2];
        }
        else if (text.EndsWith('s'))
        {
            text = text[..^1];
        }
        else if (text.EndsWith('m'))
        {
            multiplier = 60_000d;
            text = text[..^1];
        }
        else if (text.EndsWith('h'))
        {
            multiplier = 3_600_000d;
            text = text[..^1];
        }

        if (
            !double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount
            )
            || double.IsNaN(amount)
            || double.IsInfinity(amount)
            || amount <= 0
            || amount * multiplier > TimeSpan.MaxValue.TotalMilliseconds
        )
        {
            throw new FormatException(
                $"{optionName} must be a positive duration such as 500ms, 1s, 2m, or 1h."
            );
        }

        var duration = TimeSpan.FromMilliseconds(amount * multiplier);
        if (duration == TimeSpan.Zero)
        {
            throw new FormatException($"{optionName} is too small to represent.");
        }
        return duration;
    }

    private static void WriteSessionJson<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo
    ) =>
        Console
            .OpenStandardOutput()
            .Write(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(value, typeInfo) + Environment.NewLine
                )
            );

    private static async Task WriteSessionJsonAsync<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
    {
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, typeInfo) + Environment.NewLine
        );
        await Console
            .OpenStandardOutput()
            .WriteAsync(bytes, cancellationToken)
            .ConfigureAwait(false);
    }
}
