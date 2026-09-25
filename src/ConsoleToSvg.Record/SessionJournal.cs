using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public sealed record SessionJournalEntry
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset At { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phase { get; init; }
    public string? Executable { get; init; }
    public string[]? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool? WorkingDirectoryTemporary { get; init; }
    public TerminalInput[]? Inputs { get; init; }
    public string? Command { get; init; }
    public string? Text { get; init; }
    public string? Regex { get; init; }
    public string? Until { get; init; }
    public double? StableForMilliseconds { get; init; }
    public double? TimeoutMilliseconds { get; init; }
    public bool? Matched { get; init; }
    public bool? TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? OutputPath { get; init; }
    public string? OptionsJson { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionJournalEntry))]
internal sealed partial class SessionJournalJsonContext : JsonSerializerContext { }

public static class SessionJournalStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly TimeSpan AppendTimeout = TimeSpan.FromSeconds(5);

    public static string GetJournalRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "console2svg",
            "journals"
        );

    public static async Task<IReadOnlyList<SessionJournalEntry>> ReadAsync(
        string sessionId,
        CancellationToken cancellationToken = default
    )
    {
        var path = GetJournalPath(sessionId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No journal is available for managed session '{sessionId}'.",
                path
            );
        }
        if (
            !Directory.Exists(
                Path.Combine(ManagedTerminalSessionManager.GetSessionRoot(), sessionId)
            )
            && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow - Retention
        )
        {
            File.Delete(path);
            throw new FileNotFoundException(
                $"The journal for managed session '{sessionId}' has expired.",
                path
            );
        }

        var entries = new List<SessionJournalEntry>();
        await using var stream = await OpenExclusiveFileAsync(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan,
                cancellationToken
            )
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }
            try
            {
                entries.Add(
                    JsonSerializer.Deserialize(
                        line,
                        SessionJournalJsonContext.Default.SessionJournalEntry
                    ) ?? throw new InvalidDataException("A session journal entry is empty.")
                );
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"The journal for managed session '{sessionId}' is invalid.",
                    exception
                );
            }
        }

        return entries;
    }

    public static async Task AppendAsync(
        string sessionId,
        SessionJournalEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = GetJournalPath(sessionId);
        EnsurePrivateDirectory(GetJournalRoot());
        var json = JsonSerializer.Serialize(
            entry,
            SessionJournalJsonContext.Default.SessionJournalEntry
        );
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await using var stream = await OpenExclusiveFileAsync(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileOptions.Asynchronous | FileOptions.WriteThrough,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    internal static async Task CreateAsync(
        string sessionId,
        string[] command,
        string workingDirectory,
        int width,
        int height,
        string role,
        bool temporaryWorkingDirectory,
        CancellationToken cancellationToken
    )
    {
        var path = GetJournalPath(sessionId);
        EnsurePrivateDirectory(GetJournalRoot());
        await File.WriteAllTextAsync(path, string.Empty, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await AppendAsync(
                sessionId,
                new SessionJournalEntry
                {
                    Kind = "launch",
                    Name = role,
                    Executable = command[0],
                    Arguments = command[1..],
                    WorkingDirectory = workingDirectory,
                    WorkingDirectoryTemporary = temporaryWorkingDirectory,
                    Width = width,
                    Height = height,
                    At = DateTimeOffset.UtcNow,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    internal static void Delete(string sessionId)
    {
        var path = GetJournalPath(sessionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static void CleanupExpired(DateTimeOffset now)
    {
        var root = GetJournalRoot();
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "s_*.jsonl"))
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            if (
                !Directory.Exists(
                    Path.Combine(ManagedTerminalSessionManager.GetSessionRoot(), sessionId)
                )
                && File.GetLastWriteTimeUtc(path) < now.UtcDateTime - Retention
            )
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<FileStream> OpenExclusiveFileAsync(
        string path,
        FileMode mode,
        FileAccess access,
        FileOptions options,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTimeOffset.UtcNow + AppendTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, mode, access, FileShare.None, 4096, options);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static string GetJournalPath(string sessionId)
    {
        if (
            sessionId.Length != 34
            || !sessionId.StartsWith("s_", StringComparison.Ordinal)
            || sessionId.AsSpan(2).IndexOfAnyExcept("0123456789abcdef".AsSpan()) >= 0
        )
        {
            throw new ArgumentException("The managed session ID is invalid.", nameof(sessionId));
        }
        return Path.Combine(GetJournalRoot(), sessionId + ".jsonl");
    }

    private static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }
}
