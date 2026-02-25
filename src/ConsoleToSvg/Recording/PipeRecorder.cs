using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZLogger;

namespace ConsoleToSvg.Recording;

public static class PipeRecorder
{
    public static async Task<RecordingSession> RecordAsync(
        Stream input,
        int width,
        int height,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        logger ??= NullLogger.Instance;
        logger.ZLogDebug($"Start pipe recording. Width={width} Height={height}");
        var session = new RecordingSession(width, height);
        var stopwatch = Stopwatch.StartNew();

        using var reader = new StreamReader(
            input,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true
        );

        var buffer = new char[4096];
        var previousWasCarriageReturn = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (count <= 0)
            {
                logger.ZLogDebug($"Pipe input reached EOF.");
                break;
            }

            var text = NormalizeLineEndings(buffer, count, ref previousWasCarriageReturn);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, text);
            logger.ZLogDebug(
                $"Captured pipe chunk chars={count} normalizedChars={text.Length} elapsedMs={stopwatch.ElapsedMilliseconds} preview={ToPreview(text)}"
            );
        }

        logger.ZLogDebug($"Pipe recording completed. Events={session.Events.Count} ElapsedMs={stopwatch.ElapsedMilliseconds}");
        return session;
    }

    private static string NormalizeLineEndings(char[] buffer, int count, ref bool previousWasCarriageReturn)
    {
        var sb = new StringBuilder(count + 16);
        for (var i = 0; i < count; i++)
        {
            var ch = buffer[i];
            if (ch == '\n' && !previousWasCarriageReturn)
            {
                sb.Append('\r');
            }

            sb.Append(ch);
            previousWasCarriageReturn = ch == '\r';
        }

        return sb.ToString();
    }

    private static string ToPreview(string text)
    {
        var normalized = text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        return normalized.Length <= 120 ? normalized : normalized.Substring(0, 120) + "...";
    }
}