using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class AsciicastReader
{
    public static async Task<RecordingSession> ReadFromFileAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RecordingSession> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            4096,
            leaveOpen: true
        );
        var headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidDataException("Invalid asciicast: missing header line.");
        }

        var header = JsonSerializer.Deserialize(
            headerLine,
            AsciicastJsonContext.Default.AsciicastHeader
        );
        if (header is null)
        {
            throw new InvalidDataException("Invalid asciicast header JSON.");
        }

        var session = new RecordingSession(header);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            if (
                document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() < 3
            )
            {
                continue;
            }

            var time = document.RootElement[0].GetDouble();
            var type = document.RootElement[1].GetString() ?? "o";
            var data = document.RootElement[2].GetString() ?? string.Empty;
            session.Events.Add(
                new AsciicastEvent
                {
                    Time = time,
                    Type = type,
                    Data = data,
                }
            );
        }

        return session;
    }
}
