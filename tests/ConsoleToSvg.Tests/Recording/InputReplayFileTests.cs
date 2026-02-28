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
                writer.AppendEvent(new InputEvent { Time = 0.5, Key = "h", Modifiers = [], Type = "keydown" });
                writer.AppendEvent(new InputEvent { Time = 1.25, Key = "Enter", Modifiers = [], Type = "keydown" });
                writer.AppendEvent(new InputEvent { Time = 2.0, Key = "c", Modifiers = ["ctrl"], Type = "keydown" });
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
                writer.AppendEvent(new InputEvent { Time = 0.1, Key = "a", Modifiers = [], Type = "keydown" });
                writer.AppendEvent(new InputEvent { Time = 0.2, Key = "ArrowUp", Modifiers = [], Type = "keydown" });
            }

            var json = await File.ReadAllTextAsync(tmpPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            doc.RootElement.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
            var replay = doc.RootElement.GetProperty("replay");
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
        var bytes = InputReplayFile.EventToBytes(new InputEvent { Key = "ArrowUp", Modifiers = [] });
        var text = Encoding.UTF8.GetString(bytes);
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 0.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
    }

    [Test]
    public void EventToBytesEnter()
    {
        var bytes = InputReplayFile.EventToBytes(new InputEvent { Key = "Enter", Modifiers = [] });
        bytes.ShouldBe(new byte[] { 0x0d });
    }

    [Test]
    public void EventToBytesCtrlC()
    {
        var bytes = InputReplayFile.EventToBytes(new InputEvent { Key = "c", Modifiers = ["ctrl"] });
        bytes.ShouldBe(new byte[] { 0x03 });
    }

    [Test]
    public void EventToBytesShiftArrowUp()
    {
        var bytes = InputReplayFile.EventToBytes(
            new InputEvent { Key = "ArrowUp", Modifiers = ["shift"] }
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
            new InputEvent { Time = 0.0, Key = "a", Modifiers = [] },
            new InputEvent { Time = 0.0, Key = "b", Modifiers = [] },
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
        var events = new List<InputEvent> { new InputEvent { Time = 0.0, Key = "ArrowUp", Modifiers = [] } };

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
        var bytes = InputReplayFile.EventToBytes(new InputEvent { Key = "Tab", Modifiers = ["shift"] });
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
        var homeBytes = InputReplayFile.EventToBytes(new InputEvent { Key = "Home", Modifiers = [] });
        var homeText = Encoding.UTF8.GetString(homeBytes);
        var homeEvents = new List<InputEvent>(InputReplayFile.ParseInputText(homeText, 0.0));
        homeEvents.Count.ShouldBe(1);
        homeEvents[0].Key.ShouldBe("Home");

        // End → \x1b[F → parse → End
        var endBytes = InputReplayFile.EventToBytes(new InputEvent { Key = "End", Modifiers = [] });
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

    // ── Terminal response filtering ──────────────────────────────────────────

    [Test]
    public void ParseInputTextDa2ResponseIsFiltered()
    {
        // DA2 response: ESC[>0;10;1c — should be completely skipped.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("a\x1b[>0;10;1cb", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextDa1ResponseIsFiltered()
    {
        // DA1 response: ESC[?64;1;2;6;21;22c
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("x\x1b[?64;1;2;6;21;22cy", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("x");
        events[1].Key.ShouldBe("y");
    }

    [Test]
    public void ParseInputTextDecrpmResponseIsFiltered()
    {
        // DECRPM response: ESC[?12;2$y — has both private prefix '?' and intermediate byte '$'.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("a\x1b[?12;2$yb", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextMultipleTerminalResponsesAreFiltered()
    {
        // Simulate the WSL vim startup: user types "vim\r", then terminal sends responses.
        var input = "vim\r"
            + "\x1b[>0;10;1c"   // DA2 response
            + "\x1b[?12;2$y"    // DECRPM response
            + "\x1b[?64;1;2c"   // DA1 response
            + "ihello\x1b:q!\r"; // user types in vim (ESC+: = Alt+:)
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(input, 0.0));

        // ESC followed immediately by ':' is parsed as Alt+: (standard VT behavior).
        // Terminal responses (DA2, DECRPM, DA1) should all be filtered.
        var keys = events.ConvertAll(e => e.Key);
        keys.ShouldBe(
            new[] { "v", "i", "m", "Enter", "i", "h", "e", "l", "l", "o", ":", "q", "!", "Enter" }
        );
        // The ':' after ESC should have an 'alt' modifier.
        var altColon = events.Find(e => e.Key == ":");
        altColon.ShouldNotBeNull();
        altColon!.Modifiers.ShouldContain("alt");
    }

    [Test]
    public void ParseInputTextLonePrivatePrefixCsiIsFiltered()
    {
        // ESC[<0;35;1M — xterm mouse event with '<' private prefix — should be filtered.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("a\x1b[<0;35;1Mb", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextIntermediateByteOnlyIsFiltered()
    {
        // A CSI sequence with intermediate byte but no private prefix: ESC[0\"q (DECSCA)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("a\x1b[0\"qb", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextNormalCsiUnaffected()
    {
        // Normal user input CSI sequences should still work after the fix.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("\x1b[A\x1b[1;2B\x1b[15~\x1b[Z", 0.0)
        );
        events.Count.ShouldBe(4);
        events[0].Key.ShouldBe("ArrowUp");
        events[1].Key.ShouldBe("ArrowDown");
        events[1].Modifiers.ShouldContain("shift");
        events[2].Key.ShouldBe("F5");
        events[3].Key.ShouldBe("Tab");
        events[3].Modifiers.ShouldContain("shift");
    }

    // ── ParseInputTextPartial ────────────────────────────────────────────────

    [Test]
    public void ParseInputTextPartialLoneEscAtEnd()
    {
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("hello\x1b", 1.0);
        events.Count.ShouldBe(5); // h, e, l, l, o
        remainder.ShouldBe("\x1b");
    }

    [Test]
    public void ParseInputTextPartialCsiPrefixAtEnd()
    {
        // ESC[ at end → incomplete CSI
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("ab\x1b[", 1.0);
        events.Count.ShouldBe(2); // a, b
        remainder.ShouldBe("\x1b[");
    }

    [Test]
    public void ParseInputTextPartialSs3PrefixAtEnd()
    {
        // ESCO at end → incomplete SS3
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("x\x1bO", 1.0);
        events.Count.ShouldBe(1); // x
        remainder.ShouldBe("\x1bO");
    }

    [Test]
    public void ParseInputTextPartialCsiParamsNoFinalByte()
    {
        // ESC[1;2 at end → incomplete CSI with params
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("z\x1b[1;2", 1.0);
        events.Count.ShouldBe(1); // z
        remainder.ShouldBe("\x1b[1;2");
    }

    [Test]
    public void ParseInputTextPartialCompleteSequenceNotCarriedOver()
    {
        // Complete CSI → no remainder
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("a\x1b[Z", 1.0);
        events.Count.ShouldBe(2); // a, Tab(shift)
        remainder.ShouldBe("");
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("Tab");
        events[1].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void ParseInputTextPartialCompleteSs3NotCarriedOver()
    {
        // Complete SS3 → no remainder
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("a\x1bOB", 1.0);
        events.Count.ShouldBe(2); // a, ArrowDown
        remainder.ShouldBe("");
        events[1].Key.ShouldBe("ArrowDown");
    }

    [Test]
    public void ParseInputTextPartialMultipleChunksJoin()
    {
        // Simulate two ReadAsync calls: first "hello\x1b", then "[Z"
        var (events1, rem1) = InputReplayFile.ParseInputTextPartial("hello\x1b", 1.0);
        events1.Count.ShouldBe(5); // h, e, l, l, o
        rem1.ShouldBe("\x1b");

        // Prepend remainder to next chunk
        var (events2, rem2) = InputReplayFile.ParseInputTextPartial(rem1 + "[Z", 1.1);
        events2.Count.ShouldBe(1); // Tab(shift)
        events2[0].Key.ShouldBe("Tab");
        events2[0].Modifiers.ShouldContain("shift");
        rem2.ShouldBe("");
    }

    [Test]
    public void ParseInputTextPartialArrowKeysSplitAcrossChunks()
    {
        // Simulate: first "\x1b", then "OA" (ArrowUp via SS3)
        var (events1, rem1) = InputReplayFile.ParseInputTextPartial("text\x1b", 1.0);
        events1.Count.ShouldBe(4); // t, e, x, t
        rem1.ShouldBe("\x1b");

        var (events2, rem2) = InputReplayFile.ParseInputTextPartial(rem1 + "OA", 1.1);
        events2.Count.ShouldBe(1);
        events2[0].Key.ShouldBe("ArrowUp");
        rem2.ShouldBe("");
    }

    [Test]
    public void ParseInputTextPartialThreeChunkJoin()
    {
        // Simulate: "\x1b" → "[" → "A"
        var (e1, r1) = InputReplayFile.ParseInputTextPartial("a\x1b", 1.0);
        e1.Count.ShouldBe(1); // a
        r1.ShouldBe("\x1b");

        var (e2, r2) = InputReplayFile.ParseInputTextPartial(r1 + "[", 1.1);
        e2.Count.ShouldBe(0); // nothing complete yet
        r2.ShouldBe("\x1b[");

        var (e3, r3) = InputReplayFile.ParseInputTextPartial(r2 + "A", 1.2);
        e3.Count.ShouldBe(1);
        e3[0].Key.ShouldBe("ArrowUp");
        r3.ShouldBe("");
    }

    [Test]
    public void ParseInputTextPartialNoEscNoRemainder()
    {
        // Plain text → no remainder
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("hello", 1.0);
        events.Count.ShouldBe(5);
        remainder.ShouldBe("");
    }

    [Test]
    public void ParseInputTextPartialEmptyInput()
    {
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("", 1.0);
        events.Count.ShouldBe(0);
        remainder.ShouldBe("");
    }

    [Test]
    public void ParseInputTextPartialCsiWithPrivatePrefix()
    {
        // ESC[? at end → incomplete (terminal response prefix)
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("x\x1b[?", 1.0);
        events.Count.ShouldBe(1); // x
        remainder.ShouldBe("\x1b[?");
    }
}
