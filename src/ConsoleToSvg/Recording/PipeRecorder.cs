using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

public static class PipeRecorder
{
    public static async Task<RecordingSession> RecordAsync(
        Stream input,
        int width,
        int height,
        CancellationToken cancellationToken
    )
    {
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
                break;
            }

            var text = NormalizeLineEndings(buffer, count, ref previousWasCarriageReturn);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, text);
        }

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
}