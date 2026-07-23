using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class AsciicastWriter
{
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

        var headerLine = JsonSerializer.Serialize(
            session.Header,
            AsciicastJsonContext.Default.AsciicastHeader
        );
        await writer.WriteLineAsync(headerLine).ConfigureAwait(false);

        using var ms = new MemoryStream();
        foreach (var outputEvent in session.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ms.SetLength(0);
            using var jw = new Utf8JsonWriter(ms);
            jw.WriteStartArray();
            jw.WriteNumberValue(outputEvent.Time);
            jw.WriteStringValue(outputEvent.Type);
            jw.WriteStringValue(outputEvent.Data);
            jw.WriteEndArray();
            await jw.FlushAsync().ConfigureAwait(false);
            var line = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }
}
