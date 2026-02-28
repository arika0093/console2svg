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
        var helloBytes = Encoding.UTF8.GetBytes("hello");
        var worldBytes = Encoding.UTF8.GetBytes("world\r\n");

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);

        InputReplayFile.WriteEvent(writer, 0.5, helloBytes, helloBytes.Length);
        InputReplayFile.WriteEvent(writer, 1.25, worldBytes, worldBytes.Length);
        writer.Flush();

        ms.Position = 0;
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpPath, ms.ToArray());
            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(2);
            events[0].Time.ShouldBe(0.5);
            events[0].Data.ShouldBeEquivalentTo(helloBytes);
            events[1].Time.ShouldBe(1.25);
            events[1].Data.ShouldBeEquivalentTo(worldBytes);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task SpecialKeyEscapeSequenceRoundTrip()
    {
        // Up arrow = ESC [ A (bytes 0x1B 0x5B 0x41)
        var upArrow = new byte[] { 0x1B, 0x5B, 0x41 };
        // Shift+Tab = ESC [ Z (bytes 0x1B 0x5B 0x5A)
        var shiftTab = new byte[] { 0x1B, 0x5B, 0x5A };

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);

        InputReplayFile.WriteEvent(writer, 0.1, upArrow, upArrow.Length);
        InputReplayFile.WriteEvent(writer, 0.2, shiftTab, shiftTab.Length);
        writer.Flush();

        ms.Position = 0;
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpPath, ms.ToArray());
            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(2);
            events[0].Data.ShouldBeEquivalentTo(upArrow);
            events[1].Data.ShouldBeEquivalentTo(shiftTab);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task ReplayStreamReturnsEventsSequentially()
    {
        var events = new List<(double Time, byte[] Data)>
        {
            (0.0, Encoding.UTF8.GetBytes("abc")),
            (0.0, Encoding.UTF8.GetBytes("def")),
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
        var events = new List<(double Time, byte[] Data)>();
        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[16];

        var count = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

        count.ShouldBe(0);
    }

    [Test]
    public async Task ReadAllAsyncSkipsBlankLines()
    {
        var aBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("a"));
        var bBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("b"));
        var content = $"[0.1,\"{aBase64}\"]\n\n[0.2,\"{bBase64}\"]\n";
        var tmpPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpPath, content, new UTF8Encoding(false));
            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(2);
            events[0].Data.ShouldBeEquivalentTo(Encoding.UTF8.GetBytes("a"));
            events[1].Data.ShouldBeEquivalentTo(Encoding.UTF8.GetBytes("b"));
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }
}
