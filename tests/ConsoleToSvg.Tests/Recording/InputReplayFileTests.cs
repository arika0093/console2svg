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
    // ── Write / read round-trip ──────────────────────────────────────────────

    [Test]
    public async Task WriteAndReadRoundTrip()
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await using (
                var writer = new InputReplayFile.InputReplayWriter(
                    new StreamWriter(File.OpenWrite(tmpPath), new UTF8Encoding(false))
                )
            )
            {
                writer.AppendEvent(new InputEvent(0.5, "h", [], "keydown"));
                writer.AppendEvent(new InputEvent(1.25, "Enter", [], "keydown"));
                writer.AppendEvent(new InputEvent(2.0, "c", ["ctrl"], "keydown"));
            }

            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);

            events.Count.ShouldBe(3);
            events[0].Time.ShouldBe(0.5);
            events[0].Key.ShouldBe("h");
            events[0].Type.ShouldBe("keydown");
            events[0].Modifiers.Length.ShouldBe(0);
            events[1].Time.ShouldBe(1.25);
            events[1].Key.ShouldBe("Enter");
            events[2].Time.ShouldBe(2.0);
            events[2].Key.ShouldBe("c");
            events[2].Modifiers.ShouldContain("ctrl");
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task WrittenFileIsValidJsonArray()
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await using (
                var writer = new InputReplayFile.InputReplayWriter(
                    new StreamWriter(File.OpenWrite(tmpPath), new UTF8Encoding(false))
                )
            )
            {
                writer.AppendEvent(new InputEvent(0.1, "a", [], "keydown"));
                writer.AppendEvent(new InputEvent(0.2, "ArrowUp", [], "keydown"));
            }

            var json = await File.ReadAllTextAsync(tmpPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            doc.RootElement.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
            doc.RootElement.GetArrayLength().ShouldBe(2);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task EmptyWriterProducesEmptyJsonArray()
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await using (
                var writer = new InputReplayFile.InputReplayWriter(
                    new StreamWriter(File.OpenWrite(tmpPath), new UTF8Encoding(false))
                )
            )
            {
                // no events appended
            }

            var events = await InputReplayFile.ReadAllAsync(tmpPath, CancellationToken.None);
            events.Count.ShouldBe(0);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    // ── ParseInputText ───────────────────────────────────────────────────────

    [Test]
    public void ParseInputTextPrintableChars()
    {
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("hi", 1.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("h");
        events[0].Modifiers.Length.ShouldBe(0);
        events[0].Type.ShouldBe("keydown");
        events[1].Key.ShouldBe("i");
    }

    [Test]
    public void ParseInputTextArrowKeys()
    {
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[A\x1b[B\x1b[C\x1b[D", 0.0)
        );
        events.Count.ShouldBe(4);
        events[0].Key.ShouldBe("ArrowUp");
        events[1].Key.ShouldBe("ArrowDown");
        events[2].Key.ShouldBe("ArrowRight");
        events[3].Key.ShouldBe("ArrowLeft");
        foreach (var e in events)
            e.Modifiers.Length.ShouldBe(0);
    }

    [Test]
    public void ParseInputTextCtrlKeys()
    {
        // Ctrl+A = \x01, Ctrl+C = \x03, Ctrl+Z = \x1a
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x01\x03\x1a", 0.0)
        );
        events.Count.ShouldBe(3);
        events[0].Key.ShouldBe("a");
        events[0].Modifiers.ShouldContain("ctrl");
        events[1].Key.ShouldBe("c");
        events[1].Modifiers.ShouldContain("ctrl");
        events[2].Key.ShouldBe("z");
        events[2].Modifiers.ShouldContain("ctrl");
    }

    [Test]
    public void ParseInputTextSpecialKeys()
    {
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x09\x0d\x7f", 0.0)
        );
        events.Count.ShouldBe(3);
        events[0].Key.ShouldBe("Tab");
        events[1].Key.ShouldBe("Enter");
        events[2].Key.ShouldBe("Backspace");
    }

    [Test]
    public void ParseInputTextEscapeAlone()
    {
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1b", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("Escape");
        events[0].Modifiers.Length.ShouldBe(0);
    }

    [Test]
    public void ParseInputTextAltKey()
    {
        // Alt+j = ESC + j
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1bj", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("j");
        events[0].Modifiers.ShouldContain("alt");
    }

    [Test]
    public void ParseInputTextShiftArrow()
    {
        // Shift+ArrowUp = \x1b[1;2A
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1b[1;2A", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
        events[0].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void ParseInputTextFunctionKeys()
    {
        // F1 via SS3, F5 via CSI tilde
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1bOP\x1b[15~", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("F1");
        events[1].Key.ShouldBe("F5");
    }

    // ── EventToBytes ─────────────────────────────────────────────────────────

    [Test]
    public void EventToBytesArrowUpRoundTrip()
    {
        var bytes = InputReplayFile.EventToBytes(new InputEvent(0, "ArrowUp", [], "keydown"));
        var text = Encoding.UTF8.GetString(bytes);
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
    }

    [Test]
    public void EventToBytesEnter()
    {
        var bytes = InputReplayFile.EventToBytes(new InputEvent(0, "Enter", [], "keydown"));
        bytes.ShouldBe(new byte[] { 0x0d });
    }

    [Test]
    public void EventToBytesCtrlC()
    {
        var bytes = InputReplayFile.EventToBytes(
            new InputEvent(0, "c", ["ctrl"], "keydown")
        );
        bytes.ShouldBe(new byte[] { 0x03 });
    }

    [Test]
    public void EventToBytesShiftArrowUp()
    {
        var bytes = InputReplayFile.EventToBytes(
            new InputEvent(0, "ArrowUp", ["shift"], "keydown")
        );
        var text = Encoding.UTF8.GetString(bytes);
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
        events[0].Modifiers.ShouldContain("shift");
    }

    // ── ReplayStream ─────────────────────────────────────────────────────────

    [Test]
    public async Task ReplayStreamReturnsEventsSequentially()
    {
        var events = new List<InputEvent>
        {
            new InputEvent(0.0, "a", [], "keydown"),
            new InputEvent(0.0, "b", [], "keydown"),
        };

        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[16];

        var count1 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text1 = Encoding.UTF8.GetString(buffer, 0, count1);

        var count2 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text2 = Encoding.UTF8.GetString(buffer, 0, count2);

        var count3 = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

        text1.ShouldBe("a");
        text2.ShouldBe("b");
        count3.ShouldBe(0); // EOF
    }

    [Test]
    public async Task ReplayStreamReturnsZeroOnEmpty()
    {
        var events = new List<InputEvent>();
        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[16];

        var count = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

        count.ShouldBe(0);
    }

    [Test]
    public async Task ReplayStreamArrowKeyRoundTrip()
    {
        // Write ArrowUp event, replay it, parse the bytes, verify the key name survives.
        var events = new List<InputEvent>
        {
            new InputEvent(0.0, "ArrowUp", [], "keydown"),
        };

        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[32];
        var count = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text = Encoding.UTF8.GetString(buffer, 0, count);

        var parsed = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        parsed.Count.ShouldBe(1);
        parsed[0].Key.ShouldBe("ArrowUp");
    }
}

