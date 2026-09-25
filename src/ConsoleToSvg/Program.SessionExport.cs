using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Configuration;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg;

internal sealed class SessionExportOutput
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int Entries { get; init; }
}

internal static partial class Program
{
    private static async Task<int> ExportManagedSessionAsync(
        AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var sessionId =
            options.SessionId
            ?? throw new InvalidOperationException("A managed session ID is required.");
        var outputPath =
            options.SessionOutputPath
            ?? throw new InvalidOperationException("A Scenario document output path is required.");
        var entries = await SessionJournalStore
            .ReadAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        var document = CreateScenarioDocumentFromJournal(entries, sessionId);
        await DocumentStore
            .SaveScenarioAsync(outputPath, document, cancellationToken)
            .ConfigureAwait(false);
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (!Path.GetFileName(fullOutputPath).Contains('.'))
        {
            fullOutputPath += ".yaml";
        }
        WriteSessionJson(
            new SessionExportOutput
            {
                SessionId = sessionId,
                Path = fullOutputPath,
                Entries = entries.Count,
            },
            SessionOutputJsonContext.Default.SessionExportOutput
        );
        return 0;
    }

    private static ScenarioDocument CreateScenarioDocumentFromJournal(
        IReadOnlyList<SessionJournalEntry> entries,
        string sessionId
    )
    {
        var launch = entries.FirstOrDefault(entry =>
            entry.Kind == "launch" && entry.Name != "shell"
        );
        if (launch?.Executable is not { Length: > 0 } executable)
        {
            throw new InvalidDataException(
                $"Managed session '{sessionId}' has no exportable application launch."
            );
        }

        var optionsJson = entries
            .Where(entry => entry.Kind == "metadata" && entry.Name == "options")
            .Select(entry => entry.OptionsJson)
            .FirstOrDefault(json => json is not null);
        var documentOptions = optionsJson is null
            ? null
            : JsonSerializer.Deserialize(optionsJson, DocumentJsonContext.Default.ConsoleOptions);

        var actions = entries.Where(entry => entry.Kind is "action" or "condition").ToArray();
        return new ScenarioDocument
        {
            Options = documentOptions,
            Scenario = new ScenarioDefinition
            {
                WorkingDir = CreateJournalWorkingDirectory(launch),
                Prepare = actions
                    .Where(entry => entry.Phase == "prepare" && entry.Name == "command")
                    .Select(ToScenarioCommandStep)
                    .ToArray(),
                Launch = new ScenarioLaunch
                {
                    Executable = executable,
                    Args = launch.Arguments ?? [],
                    Options = new ConsoleOptions
                    {
                        Terminal =
                            launch.Width is null && launch.Height is null
                                ? null
                                : new TerminalOptions
                                {
                                    Width = launch.Width,
                                    Height = launch.Height,
                                },
                    },
                },
                Execute = actions
                    .Where(entry => entry.Phase is null or "execute")
                    .Select(ToScenarioStep)
                    .Where(step => step is not null)
                    .Cast<ScenarioStep>()
                    .ToArray(),
                Verify = actions
                    .Where(entry => entry.Phase == "verify" && entry.Name == "command")
                    .Select(ToScenarioCommandStep)
                    .ToArray(),
                Teardown = actions
                    .Where(entry => entry.Phase == "teardown" && entry.Name == "command")
                    .Select(ToScenarioCommandStep)
                    .ToArray(),
            },
        };
    }

    private static ScenarioCommandStep ToScenarioCommandStep(SessionJournalEntry entry) =>
        new() { Type = "command", Command = entry.Command };

    private static WorkingDirectoryOptions? CreateJournalWorkingDirectory(
        SessionJournalEntry launch
    )
    {
        if (launch.WorkingDirectoryTemporary == true)
        {
            return new WorkingDirectoryOptions { Temporary = true };
        }
        return launch.WorkingDirectory is { } path
            ? new WorkingDirectoryOptions { Path = path }
            : null;
    }

    private static ScenarioStep? ToScenarioStep(SessionJournalEntry entry)
    {
        if (entry.Kind == "condition")
        {
            return new ScenarioStep
            {
                Type = "wait",
                Args = new ScenarioStepArguments
                {
                    Text = entry.Text,
                    Regex = entry.Regex,
                    Until = entry.Until,
                    StableFor = FormatJournalDuration(entry.StableForMilliseconds),
                    Timeout = FormatJournalDuration(entry.TimeoutMilliseconds),
                },
            };
        }

        return entry.Name switch
        {
            "send" when entry.Inputs is { Length: > 0 } => new ScenarioStep
            {
                Type = "send",
                Inputs = entry.Inputs.Select(ToScenarioInput).ToArray(),
            },
            "resize" when entry.Width is > 0 && entry.Height is > 0 => new ScenarioStep
            {
                Type = "resize",
                Args = new ScenarioStepArguments { Width = entry.Width, Height = entry.Height },
            },
            "capture" when entry.OutputPath is not null => new ScenarioStep
            {
                Type = "capture",
                Args = new ScenarioStepArguments { Output = entry.OutputPath },
                Options = DeserializeCaptureOptions(entry.OptionsJson),
            },
            "command" when entry.Command is not null => new ScenarioStep
            {
                Type = "command",
                Command = entry.Command,
            },
            _ => null,
        };
    }

    private static ConsoleOptions? DeserializeCaptureOptions(string? json) =>
        json is null
            ? null
            : JsonSerializer.Deserialize(json, DocumentJsonContext.Default.ConsoleOptions);

    private static ScenarioInput ToScenarioInput(TerminalInput input) =>
        input.Kind switch
        {
            TerminalInputKind.Text => new ScenarioInput { Text = input.Value },
            TerminalInputKind.Key => new ScenarioInput { Keys = input.Value },
            TerminalInputKind.Paste => new ScenarioInput { Paste = input.Value },
            TerminalInputKind.RawHex => new ScenarioInput { RawHex = input.Value },
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

    private static string? FormatJournalDuration(double? milliseconds) =>
        milliseconds is null
            ? null
            : milliseconds.Value.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
}
