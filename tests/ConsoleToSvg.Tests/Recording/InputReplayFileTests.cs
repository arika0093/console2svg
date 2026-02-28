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
                var writer = new InputReplayFile.InputReplayWriter(File.OpenWrite(tmpPath))
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
    public async Task WrittenFileIsValidJsonObject()
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await using (
                var writer = new InputReplayFile.InputReplayWriter(File.OpenWrite(tmpPath))
            )
            {
                writer.AppendEvent(new InputEvent(0.1, "a", [], "keydown"));
                writer.AppendEvent(new InputEvent(0.2, "ArrowUp", [], "keydown"));
            }

            var json = await File.ReadAllTextAsync(tmpPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            doc.RootElement.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
            var replay = doc.RootElement.GetProperty("Replay");
            replay.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
            replay.GetArrayLength().ShouldBe(2);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Test]
    public async Task EmptyWriterProducesEmptyReplay()
    {
        var tmpPath = Path.GetTempFileName();
        try
        {
            await using (
                var writer = new InputReplayFile.InputReplayWriter(File.OpenWrite(tmpPath))
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
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x01\x03\x1a", 0.0));
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
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x09\x0d\x7f", 0.0));
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
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1bOP\x1b[15~", 0.0));
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
        var bytes = InputReplayFile.EventToBytes(new InputEvent(0, "c", ["ctrl"], "keydown"));
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
        var events = new List<InputEvent> { new InputEvent(0.0, "ArrowUp", [], "keydown") };

        using var stream = new InputReplayFile.ReplayStream(events);
        var buffer = new byte[32];
        var count = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);
        var text = Encoding.UTF8.GetString(buffer, 0, count);

        var parsed = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        parsed.Count.ShouldBe(1);
        parsed[0].Key.ShouldBe("ArrowUp");
    }

    // ── Windows-specific sequences ───────────────────────────────────────────

    [Test]
    public void ParseInputTextSs3HomeEnd()
    {
        // Windows Terminal sends \x1bOH for Home and \x1bOF for End in application-cursor-keys mode.
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1bOH\x1bOF", 0.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("Home");
        events[1].Key.ShouldBe("End");
    }

    [Test]
    public void ParseInputTextShiftTab()
    {
        // \x1b[Z = Back-Tab / Shift+Tab (sent by Windows Terminal and xterm alike).
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1b[Z", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("Tab");
        events[0].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void ParseInputTextCrlfProducesSingleEnter()
    {
        // Windows console may send CR+LF for Enter; should produce only one Enter event.
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\r\n", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("Enter");
    }

    [Test]
    public void ParseInputTextConsecutiveCrsProduceTwoEnters()
    {
        // Two distinct CR presses (\r then \r) should still produce two Enter events.
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\r\r", 0.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("Enter");
        events[1].Key.ShouldBe("Enter");
    }

    [Test]
    public void EventToBytesShiftTabRoundTrip()
    {
        // Shift+Tab → \x1b[Z → parse → Shift+Tab
        var bytes = InputReplayFile.EventToBytes(new InputEvent(0, "Tab", ["shift"], "keydown"));
        var text = Encoding.UTF8.GetString(bytes);
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("Tab");
        events[0].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void EventToBytesHomeEndRoundTrip()
    {
        // Home → \x1b[H → parse → Home
        var homeBytes = InputReplayFile.EventToBytes(new InputEvent(0, "Home", [], "keydown"));
        var homeText = Encoding.UTF8.GetString(homeBytes);
        var homeEvents = new List<InputEvent>(InputReplayFile.ParseInputText(homeText, 0.0));
        homeEvents.Count.ShouldBe(1);
        homeEvents[0].Key.ShouldBe("Home");

        // End → \x1b[F → parse → End
        var endBytes = InputReplayFile.EventToBytes(new InputEvent(0, "End", [], "keydown"));
        var endText = Encoding.UTF8.GetString(endBytes);
        var endEvents = new List<InputEvent>(InputReplayFile.ParseInputText(endText, 0.0));
        endEvents.Count.ShouldBe(1);
        endEvents[0].Key.ShouldBe("End");
    }

    // ── Win32-input-mode sequences ────────────────────────────────────────────

    [Test]
    public void ParseInputTextWin32PrintableKey()
    {
        // \x1b[86;47;118;1;0;1_ → Vk=86('V'), Sc=47, Uc=118('v'), Kd=1(down), Cs=0(no mods), Rc=1
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[86;47;118;1;0;1_", 0.0)
        );
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("v");
        events[0].Modifiers.Length.ShouldBe(0);
    }

    [Test]
    public void ParseInputTextWin32KeyUpSkipped()
    {
        // Kd=0 → key-up, should be skipped entirely
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[86;47;118;0;0;1_", 0.0)
        );
        events.Count.ShouldBe(0);
    }

    [Test]
    public void ParseInputTextWin32ArrowKey()
    {
        // VK_UP = 0x26 (38), VK_DOWN = 0x28 (40)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[38;72;0;1;0;1_\x1b[40;80;0;1;0;1_", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("ArrowUp");
        events[1].Key.ShouldBe("ArrowDown");
    }

    [Test]
    public void ParseInputTextWin32SpecialKeys()
    {
        // Home=0x24(36), End=0x23(35), PageUp=0x21(33), PageDown=0x22(34)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText(
                "\x1b[36;71;0;1;0;1_\x1b[35;79;0;1;0;1_\x1b[33;73;0;1;0;1_\x1b[34;81;0;1;0;1_",
                0.0
            )
        );
        events.Count.ShouldBe(4);
        events[0].Key.ShouldBe("Home");
        events[1].Key.ShouldBe("End");
        events[2].Key.ShouldBe("PageUp");
        events[3].Key.ShouldBe("PageDown");
    }

    [Test]
    public void ParseInputTextWin32CtrlKey()
    {
        // Ctrl+C: Vk=0x43(67,'C'), Uc=3(Ctrl+C ctrl-char), Kd=1, Cs=8(LeftCtrl)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[67;46;3;1;8;1_", 0.0)
        );
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("c");
        events[0].Modifiers.ShouldContain("ctrl");
    }

    [Test]
    public void ParseInputTextWin32ShiftKey()
    {
        // Shift+V: Vk=86, Uc=86('V'), Kd=1, Cs=16(0x10=Shift)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[86;47;86;1;16;1_", 0.0)
        );
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("V");
        events[0].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void ParseInputTextWin32FunctionKey()
    {
        // F1=0x70(112), F5=0x74(116), F12=0x7B(123)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText(
                "\x1b[112;59;0;1;0;1_\x1b[116;63;0;1;0;1_\x1b[123;88;0;1;0;1_",
                0.0
            )
        );
        events.Count.ShouldBe(3);
        events[0].Key.ShouldBe("F1");
        events[1].Key.ShouldBe("F5");
        events[2].Key.ShouldBe("F12");
    }

    [Test]
    public void ParseInputTextWin32EnterBackspaceEscape()
    {
        // Enter=0x0D(13), Backspace=0x08(8), Escape=0x1B(27)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText(
                "\x1b[13;28;13;1;0;1_\x1b[8;14;8;1;0;1_\x1b[27;1;27;1;0;1_",
                0.0
            )
        );
        events.Count.ShouldBe(3);
        events[0].Key.ShouldBe("Enter");
        events[1].Key.ShouldBe("Backspace");
        events[2].Key.ShouldBe("Escape");
    }

    [Test]
    public void ParseInputTextFocusEventsSkipped()
    {
        // \x1b[I = focus-in, \x1b[O = focus-out; both should be silently skipped.
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("\x1b[Ia\x1b[O", 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("a");
    }

    [Test]
    public void ParseInputTextWin32KeyUpAndDownSequence()
    {
        // Typical Win32 pair: key-down ('v'), key-up ('v') — only down event emitted.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[86;47;118;1;0;1_\x1b[86;47;118;0;0;1_", 0.0)
        );
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("v");
    }
}
