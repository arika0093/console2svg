using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Configuration;
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

/// <summary>
/// Ephemeral visual inspection result. This is a SessionJournal observation and
/// must be omitted from session export / ScenarioDocument.
/// </summary>
internal sealed class SessionInspectOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Format { get; init; } = "svg";
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
[JsonSerializable(typeof(SessionInspectOutput))]
[JsonSerializable(typeof(SessionExportOutput))]
[JsonSerializable(typeof(SessionStopAllOutput))]
[JsonSerializable(typeof(SessionErrorOutput))]
internal sealed partial class SessionOutputJsonContext : JsonSerializerContext { }

internal static partial class Program
{
    private static async Task<int> RunSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken,
        ConsoleOptions? configuration = null
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
                SessionAction.Start => await StartManagedSessionAsync(
                        options,
                        cancellationToken,
                        configuration
                    )
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
                SessionAction.Inspect => await InspectManagedSessionAsync(
                        options,
                        cancellationToken
                    )
                    .ConfigureAwait(false),
                SessionAction.Export => await ExportManagedSessionAsync(options, cancellationToken)
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
                        or AggregateException
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
        CancellationToken cancellationToken,
        ConsoleOptions? configuration
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
                cancellationToken,
                noColorEnv: options.NoColorEnv
            )
            .ConfigureAwait(false);
        var session = response.Session;
        if (configuration is not null)
        {
            try
            {
                await SessionJournalStore
                    .AppendAsync(
                        session.Id,
                        new SessionJournalEntry
                        {
                            Kind = "metadata",
                            Name = "options",
                            OptionsJson = JsonSerializer.Serialize(
                                configuration,
                                DocumentJsonContext.Default.ConsoleOptions
                            ),
                            At = DateTimeOffset.UtcNow,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception journalException)
                when (journalException
                        is IOException
                            or UnauthorizedAccessException
                            or OperationCanceledException
                            or JsonException
                )
            {
                try
                {
                    await ManagedTerminalSessionManager
                        .RequestAsync(
                            session.Id,
                            new ManagedSessionRequest { Operation = "stop" },
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception stopException)
                    when (stopException
                            is IOException
                                or InvalidOperationException
                                or TimeoutException
                    )
                {
                    throw new AggregateException(
                        "The session started but its options could not be journaled, and cleanup failed.",
                        journalException,
                        stopException
                    );
                }
                throw;
            }
        }
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
                .Where(session => includeRetained || session.State is "starting" or "running")
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
        var observation = await TerminalSessionRuntime
            .ObserveAsync(
                RequireSessionId(options),
                options.SessionStructured,
                "read",
                cancellationToken
            )
            .ConfigureAwait(false);
        var session = observation.Session;
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
        var stableFor = ParseOptionalSessionDuration(options.SessionWaitStableFor, "--stable-for");
        var timeout = ParseOptionalSessionDuration(options.SessionWaitTimeout, "--timeout");
        var result = await TerminalSessionRuntime
            .WaitAsync(
                RequireSessionId(options),
                new TerminalCondition(
                    Text: text,
                    Until: until.Equals("absent", StringComparison.OrdinalIgnoreCase)
                        ? TerminalConditionUntil.Absent
                        : TerminalConditionUntil.Present,
                    StableFor: stableFor,
                    Timeout: timeout
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
        return WriteSessionWait(
            result.Observation.Session,
            text,
            until,
            result.Result,
            result.Matched,
            result.TimedOut
        );
    }

    private static int WriteSessionWait(
        ManagedSessionSnapshot session,
        string text,
        string until,
        string result,
        bool matched,
        bool timedOut
    )
    {
        var errorCode = (timedOut, session.State) switch
        {
            (true, _) => "wait_timeout",
            (_, "unavailable") => "host_unavailable",
            _ => "session_exited",
        };
        var errorMessage = timedOut
            ? $"Timed out waiting for text '{text}'."
            : $"Session '{session.Id}' ended before the condition matched.";
        WriteSessionJson(
            new SessionWaitOutput
            {
                Status = matched ? "completed" : "error",
                Error = matched
                    ? null
                    : new SessionErrorDetail { Code = errorCode, Message = errorMessage },
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
        var response = await TerminalSessionRuntime
            .ExecuteAsync(
                RequireSessionId(options),
                new SendTerminalInputAction(
                    options
                        .SessionInputs.Select(input => new TerminalInput(
                            (input.IsText, input.IsRaw, input.IsPaste) switch
                            {
                                (true, _, _) => TerminalInputKind.Text,
                                (_, true, _) => TerminalInputKind.RawHex,
                                (_, _, true) => TerminalInputKind.Paste,
                                _ => TerminalInputKind.Key,
                            },
                            input.Value
                        ))
                        .ToArray()
                ),
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
        var response = await TerminalSessionRuntime
            .ExecuteAsync(
                RequireSessionId(options),
                new ResizeTerminalAction(options.SessionWidth, options.SessionHeight),
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

        var response = await TerminalSessionRuntime
            .ExecuteAsync(
                RequireSessionId(options),
                new CaptureTerminalAction(
                    options.OutputPath,
                    SvgRenderOptionsFactory.Create(options)
                ),
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

    // Observation (not an artifact Action): reuses the capture rendering path for
    // identical fidelity, but writes to a randomized system temp location instead of
    // a caller-chosen durable path. Its journal entry is omitted from Scenario export.
    private static async Task<int> InspectManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.IsNullOrEmpty(options.Format)
            && !options.Format.Equals("svg", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "session inspect currently supports SVG output only."
            );
        }

        var inspectPath = SessionInspectFiles.CreateInspectPath();
        var response = await TerminalSessionRuntime
            .ExecuteAsync(
                RequireSessionId(options),
                new CaptureTerminalAction(inspectPath, SvgRenderOptionsFactory.Create(options)),
                cancellationToken,
                new SessionJournalEntry
                {
                    Kind = "observation",
                    Name = "inspect",
                    At = DateTimeOffset.UtcNow,
                }
            )
            .ConfigureAwait(false);
        SessionInspectFiles.HardenPrivateFile(inspectPath);
        await WriteSessionJsonAsync(
                new SessionInspectOutput
                {
                    SessionId = response.Session.Id,
                    State = response.Session.State,
                    Path = Path.GetFullPath(inspectPath),
                    Format = "svg",
                },
                SessionOutputJsonContext.Default.SessionInspectOutput,
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
            var response = await TerminalSessionRuntime
                .ExecuteAsync(
                    RequireSessionId(options),
                    new StopTerminalAction(),
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
                await TerminalSessionRuntime
                    .ExecuteAsync(sessionId, new StopTerminalAction(), cancellationToken)
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
                await Console
                    .Error.WriteLineAsync($"{sessionId}: {exception.Message}", cancellationToken)
                    .ConfigureAwait(false);
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
            IOException
            or TimeoutException when action is SessionAction.Capture or SessionAction.Inspect =>
                "io_error",
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
        await Console
            .Error.WriteLineAsync(message.AsMemory(), CancellationToken.None)
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
