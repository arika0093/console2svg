using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class ManagedSessionPipeTests
{
    [Test]
    public async Task CaptureRetriesWhenAnotherSessionRequestOwnsThePipe()
    {
        var id = $"s_{Guid.NewGuid():N}";
        var root = ManagedTerminalSessionManager.GetSessionRoot();
        var directory = Path.Combine(root, id);
        Directory.CreateDirectory(directory);
        var pipeName = $"c2s-{Guid.NewGuid():N}";
        using var process = Process.GetCurrentProcess();
        var manifest = new ManagedSessionManifest
        {
            Id = id,
            PipeName = pipeName,
            WorkerProcessId = process.Id,
            WorkerStartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime()),
            StartedAt = DateTimeOffset.UtcNow,
        };
        var snapshot = new ManagedSessionSnapshot
        {
            Id = id,
            State = "running",
            ProcessId = process.Id,
            Width = 80,
            Height = 24,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(
                manifest,
                ManagedSessionJsonContext.Default.ManagedSessionManifest
            )
        );
        File.WriteAllText(
            Path.Combine(directory, "snapshot.json"),
            JsonSerializer.Serialize(
                snapshot,
                ManagedSessionJsonContext.Default.ManagedSessionSnapshot
            )
        );

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = ServeTwoRequestsAsync(pipeName, snapshot, cancellation.Token);
        try
        {
            var read = ManagedTerminalSessionManager.RequestAsync(
                id,
                new ManagedSessionRequest { Operation = "read", WaitMs = 1000 },
                cancellation.Token
            );
            await Task.Delay(50, cancellation.Token);
            var capture = ManagedTerminalSessionManager.RequestAsync(
                id,
                new ManagedSessionRequest { Operation = "capture", OutputPath = "unused.svg" },
                cancellation.Token
            );
            var responses = await Task.WhenAll(read, capture);

            responses[0].Success.ShouldBeTrue();
            responses[1].Success.ShouldBeTrue();
            await server;
        }
        finally
        {
            cancellation.Cancel();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task ServeTwoRequestsAsync(
        string pipeName,
        ManagedSessionSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        for (var requestIndex = 0; requestIndex < 2; requestIndex++)
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await server.WaitForConnectionAsync(cancellationToken);
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true)
            {
                AutoFlush = true,
            };
            _ = await reader.ReadLineAsync(cancellationToken);
            if (requestIndex == 0)
            {
                await Task.Delay(1200, cancellationToken);
            }

            await writer.WriteLineAsync(
                JsonSerializer
                    .Serialize(
                        new ManagedSessionResponse { Session = snapshot },
                        ManagedSessionJsonContext.Default.ManagedSessionResponse
                    )
                    .AsMemory(),
                cancellationToken
            );
        }
    }
}
