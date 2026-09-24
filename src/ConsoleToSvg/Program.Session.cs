using System;
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
    public bool TimedOut { get; init; }
    public CaptureJsonScreen Screen { get; init; } = new();
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
    public string[] Failed { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionStartOutput))]
[JsonSerializable(typeof(SessionReadOutput))]
[JsonSerializable(typeof(SessionListOutput))]
[JsonSerializable(typeof(SessionOperationOutput))]
[JsonSerializable(typeof(SessionCaptureOutput))]
[JsonSerializable(typeof(SessionStopAllOutput))]
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
                SessionAction.List => WriteManagedSessionList(options.SessionJson),
                SessionAction.Read => await ReadManagedSessionAsync(options, cancellationToken)
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
                        or UnauthorizedAccessException
                        or ArgumentException
                        or TimeoutException
                        or FormatException
                        or JsonException
            )
        {
            await Console
                .Error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None)
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
        if (options.SessionJson)
        {
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
        }
        else
        {
            await Console
                .Out.WriteLineAsync(
                    $"Started session {session.Id} (pid {session.ProcessId}).".AsMemory(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return 0;
    }

    private static int WriteManagedSessionList(bool json)
    {
        var sessions = ManagedTerminalSessionManager.List();
        var output = new SessionListOutput
        {
            Sessions = sessions
                .Select(session => new SessionSummaryOutput
                {
                    SessionId = session.Id,
                    State = session.State,
                    ProcessId = session.ProcessId,
                    ExitCode = session.ExitCode,
                    Width = session.Width,
                    Height = session.Height,
                    UpdatedAt = session.UpdatedAt,
                })
                .ToArray(),
        };
        if (json)
        {
            WriteSessionJson(output, SessionOutputJsonContext.Default.SessionListOutput);
        }
        else if (sessions.Count == 0)
        {
            Console.WriteLine("No managed sessions.");
        }
        else
        {
            foreach (var session in sessions)
            {
                Console.WriteLine(
                    $"{session.Id}\t{session.State}\t{session.Width}x{session.Height}\tpid={session.ProcessId}\texit={session.ExitCode?.ToString() ?? "-"}"
                );
            }
        }
        return 0;
    }

    private static async Task<int> ReadManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var waitMs = ParseSessionWait(options.SessionWait);
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest { Operation = "read", WaitMs = waitMs },
                cancellationToken
            )
            .ConfigureAwait(false);
        var session = response.Session;
        if (options.SessionJson)
        {
            await WriteSessionJsonAsync(
                    new SessionReadOutput
                    {
                        SessionId = session.Id,
                        State = session.State,
                        ProcessId = session.ProcessId,
                        ExitCode = session.ExitCode,
                        Version = session.Version,
                        TimedOut = response.TimedOut,
                        Screen = new CaptureJsonScreen
                        {
                            Width = session.Width,
                            Height = session.Height,
                            Text = session.Text,
                            Truncated = session.TextTruncated,
                        },
                    },
                    SessionOutputJsonContext.Default.SessionReadOutput,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            await Console
                .Out.WriteLineAsync(session.Text.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task<int> SendManagedSessionInputAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var response = await ManagedTerminalSessionManager
            .RequestAsync(
                RequireSessionId(options),
                new ManagedSessionRequest
                {
                    Operation = "send",
                    Text = options.SessionText,
                    Key = options.SessionKey,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        return WriteSessionOperation(options, response.Session);
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
        return WriteSessionOperation(options, response.Session);
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
        if (options.SessionJson)
        {
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
        }
        else
        {
            await Console
                .Out.WriteLineAsync(
                    $"Generated: {Path.GetFullPath(options.OutputPath)}".AsMemory(),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
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
            return WriteSessionOperation(options, response.Session);
        }

        var active = ManagedTerminalSessionManager
            .List()
            .Where(session => session.State is "running" or "starting")
            .ToArray();
        if (active.Length == 0)
        {
            if (options.SessionJson)
            {
                await WriteSessionJsonAsync(
                        new SessionStopAllOutput(),
                        SessionOutputJsonContext.Default.SessionStopAllOutput,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine("No active managed sessions.");
            }
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
                return 1;
            }
        }

        var stopped = new System.Collections.Generic.List<string>();
        var failed = new System.Collections.Generic.List<string>();
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
                failed.Add($"{sessionId}: {exception.Message}");
            }
        }

        if (options.SessionJson)
        {
            await WriteSessionJsonAsync(
                    new SessionStopAllOutput
                    {
                        Stopped = stopped.ToArray(),
                        Failed = failed.ToArray(),
                    },
                    SessionOutputJsonContext.Default.SessionStopAllOutput,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else
        {
            Console.WriteLine($"Stopped {stopped.Count} managed session(s).");
            foreach (var error in failed)
            {
                await Console
                    .Error.WriteLineAsync(error.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return failed.Count == 0 ? 0 : 1;
    }

    private static int WriteSessionOperation(AppOptions options, ManagedSessionSnapshot session)
    {
        if (options.SessionJson)
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
        }
        else
        {
            Console.WriteLine($"Session {session.Id}: {session.State}.");
        }
        return 0;
    }

    private static string RequireSessionId(AppOptions options) =>
        options.SessionId
        ?? throw new InvalidOperationException("A managed session ID is required.");

    private static int ParseSessionWait(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
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
        )
        {
            throw new FormatException(
                "--wait must be a positive duration such as 500ms, 1s, or 1m."
            );
        }

        var milliseconds = Math.Ceiling(amount * multiplier);
        if (milliseconds > 60_000)
        {
            throw new FormatException("--wait must not exceed 60 seconds.");
        }
        return (int)milliseconds;
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
