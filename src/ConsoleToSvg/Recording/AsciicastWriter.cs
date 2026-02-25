using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class AsciicastWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    public static async Task WriteToFileAsync(
        string path,
        RecordingSession session,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await WriteAsync(stream, session, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        Stream stream,
        RecordingSession session,
        CancellationToken cancellationToken
    )
    {
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            4096,
            leaveOpen: true
        );

        var headerLine = JsonSerializer.Serialize(session.Header, JsonOptions);
        await writer.WriteLineAsync(headerLine).ConfigureAwait(false);

        foreach (var outputEvent in session.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = new object[] { outputEvent.Time, outputEvent.Type, outputEvent.Data };
            var line = JsonSerializer.Serialize(payload, JsonOptions);
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }
}
