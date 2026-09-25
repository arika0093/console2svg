using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Configuration;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg;

internal sealed class ScenarioRunOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string Status { get; init; } = "completed";
    public string SessionId { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public string[] Artifacts { get; init; } = [];
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase
)]
[System.Text.Json.Serialization.JsonSerializable(typeof(ScenarioRunOutput))]
internal sealed partial class ScenarioRunJsonContext
    : System.Text.Json.Serialization.JsonSerializerContext { }

internal static partial class Program
{
    private static async Task<int> RunScenarioAsync(
        AppOptions options,
        ResolvedSettings configOptions,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await RunScenarioCoreAsync(options, configOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or FormatException
                        or ArgumentException
                        or InvalidOperationException
                        or UnauthorizedAccessException
                        or TimeoutException
                        or JsonException
                        or AggregateException
                        or OperationCanceledException
            )
        {
            var message =
                exception is OperationCanceledException
                    ? "Scenario execution was cancelled."
                    : exception.Message;
            await WriteSessionErrorAsync("scenario_failed", message, CancellationToken.None)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunScenarioCoreAsync(
        AppOptions options,
        ResolvedSettings configOptions,
        CancellationToken cancellationToken
    )
    {
        var scenarioPath =
            options.ScenarioPath
            ?? throw new InvalidOperationException("A Scenario document path is required.");
        var document = DocumentStore.LoadScenario(scenarioPath);
        var scenario = document.Scenario!;
        var launch = scenario.Launch!;
        var scenarioOptions = configOptions.WithScenario(document.Options);
        var effectiveLaunchOptions = scenarioOptions.WithLaunch(launch.Options);
        var terminalOptions = new AppOptions
        {
            Workflow = Workflow.Session,
            RequestedSessionAction = SessionAction.Start,
            SessionWidth = options.SessionWidth,
            SessionHeight = options.SessionHeight,
            IsSessionWidthExplicit = options.IsSessionWidthExplicit,
            IsSessionHeightExplicit = options.IsSessionHeightExplicit,
            NoColorEnv = options.NoColorEnv,
            IsNoColorEnvExplicit = options.IsNoColorEnvExplicit,
            NoDeleteEnvs = options.NoDeleteEnvs,
            IsNoDeleteEnvsExplicit = options.IsNoDeleteEnvsExplicit,
        };
        effectiveLaunchOptions.ApplyTo(terminalOptions);
        if (
            terminalOptions.SessionWidth is < 1 or > 500
            || terminalOptions.SessionHeight is < 1 or > 500
        )
        {
            throw new InvalidOperationException("Terminal dimensions must be between 1 and 500.");
        }

        string? temporaryWorkingDirectory = null;
        var workingDirectory = ResolveScenarioWorkingDirectory(
            scenario.WorkingDir,
            out temporaryWorkingDirectory
        );
        string? sessionId = null;
        var launchStarted = false;
        var launchFinished = false;
        var launchExitCode = (int?)null;
        string? launchMarker = null;
        var workingDirectorySafeToDelete = true;
        var artifacts = new List<string>();
        string? failure = null;
        string? teardownFailure = null;

        try
        {
            var shell = InteractiveRecorder.GetDefaultShellCommand();
            var session = await ManagedTerminalSessionManager
                .StartAsync(
                    shell,
                    terminalOptions.SessionWidth,
                    terminalOptions.SessionHeight,
                    workingDirectory,
                    terminalOptions.NoDeleteEnvs,
                    cancellationToken,
                    journalRole: "shell",
                    noColorEnv: terminalOptions.NoColorEnv,
                    temporaryWorkingDirectory: temporaryWorkingDirectory is not null
                )
                .ConfigureAwait(false);
            sessionId = session.Session.Id;
            workingDirectorySafeToDelete = false;
            await SessionJournalStore
                .AppendAsync(
                    sessionId,
                    new SessionJournalEntry
                    {
                        Kind = "metadata",
                        Name = "options",
                        OptionsJson = HasConfiguredOptions(scenarioOptions.Options)
                            ? JsonSerializer.Serialize(
                                scenarioOptions.Options,
                                DocumentJsonContext.Default.ConsoleOptions
                            )
                            : null,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            foreach (var command in (scenario.Prepare ?? []).Select(command => command.Command!))
            {
                var exitCode = await RunScenarioShellCommandAsync(
                        sessionId,
                        shell[0],
                        command,
                        "prepare",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"prepare command exited with code {exitCode}: {command}"
                    );
                }
            }

            launchMarker = CreateScenarioMarker();
            launchStarted = true;
            await SendScenarioLineAsync(
                    sessionId,
                    BuildLaunchCommand(
                        shell[0],
                        launch.Executable!,
                        launch.Args ?? [],
                        launchMarker
                    ),
                    new SessionJournalEntry
                    {
                        Kind = "launch",
                        Name = "application",
                        Executable = launch.Executable,
                        Arguments = launch.Args ?? [],
                        WorkingDirectory = workingDirectory,
                        WorkingDirectoryTemporary = temporaryWorkingDirectory is not null,
                        Width = terminalOptions.SessionWidth,
                        Height = terminalOptions.SessionHeight,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            var captureIndex = 0;
            foreach (var step in scenario.Execute ?? [])
            {
                if (!launchFinished)
                {
                    (launchFinished, launchExitCode) = await CheckScenarioLaunchExitAsync(
                            sessionId,
                            launchMarker,
                            launchExitCode,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                switch (step.Type)
                {
                    case "send":
                        if (launchFinished)
                        {
                            throw new InvalidOperationException(
                                "A send step cannot run after the launched process has exited."
                            );
                        }
                        await TerminalSessionRuntime
                            .ExecuteAsync(
                                sessionId,
                                new SendTerminalInputAction(
                                    step.Inputs!.Select(ToTerminalInput).ToArray()
                                ),
                                cancellationToken,
                                new SessionJournalEntry
                                {
                                    Kind = "action",
                                    Name = "send",
                                    Phase = "execute",
                                    Inputs = step.Inputs!.Select(ToTerminalInput).ToArray(),
                                    At = DateTimeOffset.UtcNow,
                                }
                            )
                            .ConfigureAwait(false);
                        break;
                    case "wait":
                        if (launchFinished)
                        {
                            throw new InvalidOperationException(
                                "A wait step cannot run after the launched process has exited."
                            );
                        }
                        var condition = new TerminalCondition(
                            Text: step.Args!.Text,
                            Regex: step.Args.Regex,
                            Until: step.Args.Until == "absent"
                                ? TerminalConditionUntil.Absent
                                : TerminalConditionUntil.Present,
                            StableFor: ParseOptionalSessionDuration(
                                step.Args.StableFor,
                                "scenario.execute.args.stable-for"
                            ),
                            Timeout: ParseOptionalSessionDuration(
                                step.Args.Timeout,
                                "scenario.execute.args.timeout"
                            )
                        );
                        var waitResult = await TerminalSessionRuntime
                            .WaitAsync(sessionId, condition, cancellationToken, phase: "execute")
                            .ConfigureAwait(false);
                        if (!waitResult.Matched)
                        {
                            throw new InvalidOperationException(
                                waitResult.TimedOut
                                    ? "Scenario wait step timed out."
                                    : "The managed session ended before a Scenario wait condition matched."
                            );
                        }
                        break;
                    case "resize":
                        if (launchFinished)
                        {
                            throw new InvalidOperationException(
                                "A resize step cannot run after the launched process has exited."
                            );
                        }
                        await TerminalSessionRuntime
                            .ExecuteAsync(
                                sessionId,
                                new ResizeTerminalAction(
                                    step.Args!.Width!.Value,
                                    step.Args.Height!.Value
                                ),
                                cancellationToken,
                                new SessionJournalEntry
                                {
                                    Kind = "action",
                                    Name = "resize",
                                    Phase = "execute",
                                    Width = step.Args.Width,
                                    Height = step.Args.Height,
                                    At = DateTimeOffset.UtcNow,
                                }
                            )
                            .ConfigureAwait(false);
                        break;
                    case "capture":
                        if (launchFinished)
                        {
                            throw new InvalidOperationException(
                                "A capture step cannot run after the launched process has exited."
                            );
                        }
                        captureIndex++;
                        var outputPath = ResolveScenarioCapturePath(
                            step.Args?.Output,
                            scenarioPath,
                            captureIndex
                        );
                        var captureOptions = scenarioOptions.WithCapture(step.Options);
                        var captureAppOptions = new AppOptions
                        {
                            Workflow = Workflow.Session,
                            RequestedSessionAction = SessionAction.Capture,
                            OutputPath = outputPath,
                        };
                        captureOptions.ApplyTo(captureAppOptions);
                        if (captureAppOptions.Mode == OutputMode.Video)
                        {
                            throw new InvalidOperationException(
                                "Scenario capture steps currently support a single SVG screen, not video mode."
                            );
                        }
                        var extension = Path.GetExtension(outputPath);
                        if (
                            extension.Length > 0
                            && !extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            throw new InvalidOperationException(
                                "Scenario capture steps currently support SVG output only."
                            );
                        }
                        await TerminalSessionRuntime
                            .ExecuteAsync(
                                sessionId,
                                new CaptureTerminalAction(
                                    outputPath,
                                    SvgRenderOptionsFactory.Create(captureAppOptions)
                                ),
                                cancellationToken,
                                new SessionJournalEntry
                                {
                                    Kind = "action",
                                    Name = "capture",
                                    Phase = "execute",
                                    OutputPath = outputPath,
                                    OptionsJson = HasConfiguredOptions(captureOptions.Options)
                                        ? JsonSerializer.Serialize(
                                            captureOptions.Options,
                                            DocumentJsonContext.Default.ConsoleOptions
                                        )
                                        : null,
                                    At = DateTimeOffset.UtcNow,
                                }
                            )
                            .ConfigureAwait(false);
                        artifacts.Add(Path.GetFullPath(outputPath));
                        break;
                    case "command":
                        if (!launchFinished)
                        {
                            (launchFinished, launchExitCode) = await WaitForScenarioLaunchExitAsync(
                                    sessionId,
                                    launchMarker,
                                    cancellationToken
                                )
                                .ConfigureAwait(false);
                        }
                        if (launchExitCode != 0)
                        {
                            throw new InvalidOperationException(
                                $"The launched process exited with code {launchExitCode}."
                            );
                        }
                        var commandExitCode = await RunScenarioShellCommandAsync(
                                sessionId,
                                shell[0],
                                step.Command!,
                                "execute",
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        if (commandExitCode != 0)
                        {
                            throw new InvalidOperationException(
                                $"execute command exited with code {commandExitCode}: {step.Command}"
                            );
                        }
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported Scenario execution step '{step.Type}'."
                        );
                }
            }

            if (!launchFinished)
            {
                (launchFinished, launchExitCode) = await WaitForScenarioLaunchExitAsync(
                        sessionId,
                        launchMarker,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            if (launchExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The launched process exited with code {launchExitCode}."
                );
            }

            foreach (var command in (scenario.Verify ?? []).Select(command => command.Command!))
            {
                var exitCode = await RunScenarioShellCommandAsync(
                        sessionId,
                        shell[0],
                        command,
                        "verify",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"verify command exited with code {exitCode}: {command}"
                    );
                }
            }
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
                        or OperationCanceledException
            )
        {
            failure =
                exception is OperationCanceledException
                    ? "Scenario execution was cancelled."
                    : exception.Message;
        }
        finally
        {
            if (sessionId is not null)
            {
                if (launchStarted && !launchFinished)
                {
                    try
                    {
                        await TerminalSessionRuntime
                            .ExecuteAsync(
                                sessionId,
                                new SendTerminalInputAction([
                                    new TerminalInput(TerminalInputKind.Key, "Ctrl+C"),
                                ]),
                                CancellationToken.None,
                                new SessionJournalEntry
                                {
                                    Kind = "action",
                                    Name = "send",
                                    Phase = "execute",
                                    Inputs = [new TerminalInput(TerminalInputKind.Key, "Ctrl+C")],
                                    At = DateTimeOffset.UtcNow,
                                }
                            )
                            .ConfigureAwait(false);
                        using var interruptTimeout = new CancellationTokenSource(
                            TimeSpan.FromSeconds(5)
                        );
                        (launchFinished, launchExitCode) = await WaitForScenarioLaunchExitAsync(
                                sessionId,
                                launchMarker!,
                                interruptTimeout.Token
                            )
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                        when (exception
                                is InvalidOperationException
                                    or IOException
                                    or TimeoutException
                                    or OperationCanceledException
                        )
                    {
                        teardownFailure =
                            "The launched process could not be stopped in the PTY; teardown commands were skipped.";
                    }
                }

                if (!launchStarted || launchFinished)
                {
                    using var teardownTimeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(30)
                    );
                    foreach (
                        var command in (scenario.Teardown ?? []).Select(command => command.Command!)
                    )
                    {
                        try
                        {
                            var exitCode = await RunScenarioShellCommandAsync(
                                    sessionId,
                                    InteractiveRecorder.GetDefaultShellCommand()[0],
                                    command,
                                    "teardown",
                                    teardownTimeout.Token
                                )
                                .ConfigureAwait(false);
                            if (exitCode != 0)
                            {
                                teardownFailure =
                                    $"teardown command exited with code {exitCode}: {command}";
                                break;
                            }
                        }
                        catch (Exception exception)
                            when (exception
                                    is InvalidOperationException
                                        or IOException
                                        or TimeoutException
                                        or OperationCanceledException
                                        or UnauthorizedAccessException
                            )
                        {
                            teardownFailure = $"teardown failed: {exception.Message}";
                            break;
                        }
                    }
                }

                try
                {
                    await TerminalSessionRuntime
                        .ExecuteAsync(sessionId, new StopTerminalAction(), CancellationToken.None)
                        .ConfigureAwait(false);
                    workingDirectorySafeToDelete = true;
                }
                catch (Exception exception)
                    when (exception
                            is InvalidOperationException
                                or IOException
                                or TimeoutException
                                or UnauthorizedAccessException
                                or ArgumentException
                                or AggregateException
                    )
                {
                    workingDirectorySafeToDelete = !Directory.Exists(
                        Path.Combine(ManagedTerminalSessionManager.GetSessionRoot(), sessionId)
                    );
                    teardownFailure ??= $"session stop failed: {exception.Message}";
                }
            }

            if (
                temporaryWorkingDirectory is not null
                && Directory.Exists(temporaryWorkingDirectory)
            )
            {
                if (!workingDirectorySafeToDelete)
                {
                    teardownFailure ??=
                        "The temporary working directory was retained because the PTY session could not be stopped.";
                }
                else
                {
                    try
                    {
                        Directory.Delete(temporaryWorkingDirectory, recursive: true);
                    }
                    catch (Exception exception)
                        when (exception is IOException or UnauthorizedAccessException)
                    {
                        teardownFailure ??=
                            $"temporary working directory cleanup failed: {exception.Message}";
                    }
                }
            }
        }

        if (teardownFailure is not null)
        {
            failure = failure is null ? teardownFailure : failure + " " + teardownFailure;
        }
        if (failure is not null)
        {
            await WriteSessionErrorAsync("scenario_failed", failure, CancellationToken.None)
                .ConfigureAwait(false);
            return 1;
        }

        WriteSessionJson(
            new ScenarioRunOutput
            {
                SessionId = sessionId!,
                ExitCode = launchExitCode,
                Artifacts = artifacts.ToArray(),
            },
            ScenarioRunJsonContext.Default.ScenarioRunOutput
        );
        return 0;
    }

    private static string ResolveScenarioWorkingDirectory(
        WorkingDirectoryOptions? options,
        out string? temporaryDirectory
    )
    {
        temporaryDirectory = null;
        if (options?.Temporary == true)
        {
            var directory = Directory.CreateTempSubdirectory("console2svg-scenario-");
            temporaryDirectory = directory.FullName;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
            }
            return temporaryDirectory;
        }

        if (options?.Path is null)
        {
            return Environment.CurrentDirectory;
        }
        var path = Path.GetFullPath(options.Path, Environment.CurrentDirectory);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Scenario working directory does not exist: {path}"
            );
        }
        return path;
    }

    private static string ResolveScenarioCapturePath(
        string? output,
        string scenarioPath,
        int captureIndex
    )
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return Path.GetFullPath(output, Environment.CurrentDirectory);
        }

        var name = Path.GetFileNameWithoutExtension(scenarioPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "scenario";
        }
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }
        return Path.Combine(Environment.CurrentDirectory, $"{name}-capture-{captureIndex}.svg");
    }

    private static TerminalInput ToTerminalInput(ScenarioInput input)
    {
        if (input.Text is { } text)
        {
            return new TerminalInput(TerminalInputKind.Text, text);
        }
        if (input.Keys is { } keys)
        {
            return new TerminalInput(TerminalInputKind.Key, keys);
        }
        if (input.Paste is { } paste)
        {
            return new TerminalInput(TerminalInputKind.Paste, paste);
        }
        return new TerminalInput(TerminalInputKind.RawHex, input.RawHex!);
    }

    private static async Task SendScenarioLineAsync(
        string sessionId,
        string commandLine,
        SessionJournalEntry journalEntry,
        CancellationToken cancellationToken
    )
    {
        await TerminalSessionRuntime
            .ExecuteAsync(
                sessionId,
                new SendTerminalInputAction([
                    new TerminalInput(TerminalInputKind.Text, commandLine),
                    new TerminalInput(TerminalInputKind.Key, "Enter"),
                ]),
                cancellationToken,
                journalEntry
            )
            .ConfigureAwait(false);
    }

    private static async Task<int> RunScenarioShellCommandAsync(
        string sessionId,
        string shell,
        string command,
        string phase,
        CancellationToken cancellationToken
    )
    {
        var marker = CreateScenarioMarker();
        var (line, temporaryScript) = BuildShellCommand(shell, command, marker);
        try
        {
            await SendScenarioLineAsync(
                    sessionId,
                    line,
                    new SessionJournalEntry
                    {
                        Kind = "action",
                        Name = "command",
                        Phase = phase,
                        Command = command,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            var result = await TerminalSessionRuntime
                .WaitAsync(
                    sessionId,
                    new TerminalCondition(Regex: GetScenarioMarkerPattern(marker)),
                    cancellationToken,
                    recordCondition: false
                )
                .ConfigureAwait(false);
            var exitCode = ReadScenarioExitCode(result, marker);
            await SessionJournalStore
                .AppendAsync(
                    sessionId,
                    new SessionJournalEntry
                    {
                        Kind = "command-result",
                        Name = "command",
                        Phase = phase,
                        ExitCode = exitCode,
                        At = DateTimeOffset.UtcNow,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return exitCode;
        }
        finally
        {
            if (temporaryScript is not null && File.Exists(temporaryScript))
            {
                File.Delete(temporaryScript);
            }
        }
    }

    private static async Task<(bool Finished, int? ExitCode)> CheckScenarioLaunchExitAsync(
        string sessionId,
        string marker,
        int? previousExitCode,
        CancellationToken cancellationToken
    )
    {
        var observation = await TerminalSessionRuntime
            .ObserveAsync(sessionId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!TryReadScenarioExitCode(observation.Session.Text, marker, out var exitCode))
        {
            return (false, previousExitCode);
        }
        await RecordScenarioLaunchExitAsync(sessionId, exitCode, cancellationToken)
            .ConfigureAwait(false);
        return (true, exitCode);
    }

    private static async Task<(bool Finished, int? ExitCode)> WaitForScenarioLaunchExitAsync(
        string sessionId,
        string marker,
        CancellationToken cancellationToken
    )
    {
        var wait = await TerminalSessionRuntime
            .WaitAsync(
                sessionId,
                new TerminalCondition(Regex: GetScenarioMarkerPattern(marker)),
                cancellationToken,
                phase: "execute",
                recordCondition: false
            )
            .ConfigureAwait(false);
        if (!wait.Matched)
        {
            throw new InvalidOperationException(
                "The managed session ended before the launched process completed."
            );
        }
        var exitCode = ReadScenarioExitCode(wait, marker);
        await RecordScenarioLaunchExitAsync(sessionId, exitCode, cancellationToken)
            .ConfigureAwait(false);
        return (true, exitCode);
    }

    private static int ReadScenarioExitCode(TerminalConditionResult result, string marker)
    {
        if (
            !result.Matched
            || !TryReadScenarioExitCode(result.Observation.Session.Text, marker, out var exitCode)
        )
        {
            throw new InvalidOperationException(
                $"The PTY shell exited before command completion marker '{marker}' was observed."
            );
        }
        return exitCode;
    }

    private static bool TryReadScenarioExitCode(string text, string marker, out int exitCode)
    {
        exitCode = 0;
        var match = Regex.Match(
            text,
            GetScenarioMarkerPattern(marker),
            RegexOptions.CultureInvariant | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250)
        );
        return match.Success
            && int.TryParse(
                match.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out exitCode
            );
    }

    private static Task RecordScenarioLaunchExitAsync(
        string sessionId,
        int exitCode,
        CancellationToken cancellationToken
    ) =>
        SessionJournalStore.AppendAsync(
            sessionId,
            new SessionJournalEntry
            {
                Kind = "launch-result",
                Name = "application",
                ExitCode = exitCode,
                At = DateTimeOffset.UtcNow,
            },
            cancellationToken
        );

    private static string CreateScenarioMarker() =>
        "C2S" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static string GetScenarioMarkerPattern(string marker) =>
        @"(?m)^" + Regex.Escape(marker) + @":([0-9]{1,10})\s*$";

    private static string BuildLaunchCommand(
        string shell,
        string executable,
        string[] arguments,
        string marker
    )
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var encodedScript = EncodePowerShellLaunch(executable, arguments, marker);
            return $"powershell.exe -NoLogo -NoProfile -EncodedCommand {encodedScript}";
        }

        var shellName = Path.GetFileName(shell);
        if (shellName.Contains("fish", StringComparison.OrdinalIgnoreCase))
        {
            var command = BuildPosixCommand(executable, arguments);
            return $"{command}; set __c2s_status $status; printf '\\n{marker}:%s\\n' $__c2s_status";
        }
        if (
            shellName.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || shellName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
        )
        {
            var command = BuildPowerShellCommand(executable, arguments);
            return $"{command}; $status=$LASTEXITCODE; [Console]::WriteLine('{marker}:' + $status)";
        }

        var posixCommand = BuildPosixCommand(executable, arguments);
        return $"{posixCommand}; __c2s_status=$?; printf '\\n{marker}:%s\\n' \"$__c2s_status\"";
    }

    private static (string Line, string? TemporaryScript) BuildShellCommand(
        string shell,
        string command,
        string marker
    )
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var script = Path.Combine(
                Path.GetTempPath(),
                $"console2svg-scenario-{Guid.NewGuid():N}.cmd"
            );
            var content =
                "@echo off\r\n"
                + command
                + "\r\nset \"__c2s_status=%ERRORLEVEL%\"\r\necho "
                + marker
                + ":%__c2s_status%\r\n";
            File.WriteAllText(script, content, new UTF8Encoding(true));
            return ($"cmd.exe /d /c call \"{script}\"", script);
        }

        var shellName = Path.GetFileName(shell);
        if (shellName.Contains("fish", StringComparison.OrdinalIgnoreCase))
        {
            return (
                $"{command}; set __c2s_status $status; printf '\\n{marker}:%s\\n' $__c2s_status",
                null
            );
        }
        if (
            shellName.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || shellName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (
                $"{command}; $status=$LASTEXITCODE; [Console]::WriteLine('{marker}:' + $status)",
                null
            );
        }

        return ($"{command}; __c2s_status=$?; printf '\\n{marker}:%s\\n' \"$__c2s_status\"", null);
    }

    private static string BuildPosixCommand(string executable, string[] arguments) =>
        string.Join(' ', new[] { QuotePosix(executable) }.Concat(arguments.Select(QuotePosix)));

    private static string QuotePosix(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string BuildPowerShellCommand(string executable, string[] arguments) =>
        "& "
        + QuotePowerShell(executable)
        + (
            arguments.Length == 0
                ? string.Empty
                : " " + string.Join(' ', arguments.Select(QuotePowerShell))
        );

    private static string QuotePowerShell(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string EncodePowerShellLaunch(
        string executable,
        string[] arguments,
        string marker
    )
    {
        var script =
            "$exe="
            + QuotePowerShell(executable)
            + "; "
            + "$c2sArgs=@("
            + string.Join(',', arguments.Select(QuotePowerShell))
            + "); & $exe @c2sArgs; $status=$LASTEXITCODE; "
            + "[Console]::WriteLine('"
            + marker
            + ":' + $status)";
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    private static bool HasConfiguredOptions(ConsoleOptions options) =>
        options.Terminal is not null
        || options.Environment is not null
        || options.Interactive is not null
        || options.LiveServer is not null
        || options.Capture is not null
        || options.Render is not null
        || options.Converter is not null
        || options.Appearance is not null;
}
