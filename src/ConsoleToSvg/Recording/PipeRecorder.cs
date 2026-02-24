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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (count <= 0)
            {
                break;
            }

            var text = new string(buffer, 0, count);
            session.AddEvent(stopwatch.Elapsed.TotalSeconds, text);
        }

        return session;
    }
}