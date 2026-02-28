using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class InputReplayFileTests
{
    [Test]
    public async Task WriteAndReadRoundTrip()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);

        InputReplayFile.WriteEvent(writer, 0.5, "hello");
        InputReplayFile.WriteEvent(writer, 1.25, "world\r\n");
        writer.Flush();

        ms.Position = 0;
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpPath, ms.ToArray());
            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(2);
            events[0].Time.ShouldBe(0.5);
            events[0].Data.ShouldBe("hello");
            events[1].Time.ShouldBe(1.25);
            events[1].Data.ShouldBe("world\r\n");
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task ReplayStreamReturnsEventsSequentially()
    {
        var events = new List<(double Time, string Data)>
        {
            (0.0, "abc"),
            (0.0, "def"),
        };

        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[16];

        var count1 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text1 = Encoding.UTF8.GetString(buffer, 0, count1);

        var count2 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text2 = Encoding.UTF8.GetString(buffer, 0, count2);

        var count3 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

        text1.ShouldBe("abc");
        text2.ShouldBe("def");
        count3.ShouldBe(0); // EOF
    }

    [Test]
    public async Task ReplayStreamReturnsZeroOnEmpty()
    {
        var events = new List<(double Time, string Data)>();
        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[16];

        var count = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

        count.ShouldBe(0);
    }

    [Test]
    public async Task ReadAllAsyncSkipsBlankLines()
    {
        var content = "[0.1,\"a\"]\n\n[0.2,\"b\"]\n";
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpPath, content, new UTF8Encoding(false));
            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(2);
            events[0].Data.ShouldBe("a");
            events[1].Data.ShouldBe("b");
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }
}
