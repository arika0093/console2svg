using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class AsciicastWriter
{
    private static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);

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
        var buffer = new ArrayBufferWriter<byte>(4096);
        using var jsonWriter = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { SkipValidation = false }
        );
        JsonSerializer.Serialize(
            jsonWriter,
            session.Header,
            AsciicastJsonContext.Default.AsciicastHeader
        );
        await jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream
            .WriteAsync(buffer.WrittenMemory, cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(NewLineBytes, cancellationToken).ConfigureAwait(false);

        foreach (var outputEvent in session.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Clear();
            jsonWriter.Reset(buffer);
            jsonWriter.WriteStartArray();
            jsonWriter.WriteNumberValue(outputEvent.Time);
            jsonWriter.WriteStringValue(outputEvent.Type);
            jsonWriter.WriteStringValue(outputEvent.Data);
            jsonWriter.WriteEndArray();
            await jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream
                .WriteAsync(buffer.WrittenMemory, cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync(NewLineBytes, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Serializes an asciicast v2 stream and returns its UTF-8 bytes as Base64.</summary>
    public static async Task<string> WriteBase64Async(
        RecordingSession session,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        await WriteAsync(stream, session, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }
}
