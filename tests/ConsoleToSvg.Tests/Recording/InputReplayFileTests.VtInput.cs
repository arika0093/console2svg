using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed partial class InputReplayFileTests
{
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
        var bytes = InputReplayFile.EventToBytes(
            new InputEvent { Key = "Tab", Modifiers = ["shift"] }
        );
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
        var homeBytes = InputReplayFile.EventToBytes(
            new InputEvent { Key = "Home", Modifiers = [] }
        );
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
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("a\x1b[>0;10;1cb", 0.0));
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
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("a\x1b[?12;2$yb", 0.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextMultipleTerminalResponsesAreFiltered()
    {
        // Simulate the WSL vim startup: user types "vim\r", then terminal sends responses.
        var input =
            "vim\r"
            + "\x1b[>0;10;1c" // DA2 response
            + "\x1b[?12;2$y" // DECRPM response
            + "\x1b[?64;1;2c" // DA1 response
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
    public void ParseInputTextOscResponseIsFiltered()
    {
        // OSC response: ESC]10;rgb:e6e6/eded/f3f3 ST(ESC\)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("a\x1b]10;rgb:e6e6/eded/f3f3\x1b\\b", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextDcsResponseIsFiltered()
    {
        // DCS response: ESC P ... ST(ESC\)
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("x\x1bP>|xterm.js(6.1.0-beta.109)\x1b\\y", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("x");
        events[1].Key.ShouldBe("y");
    }

    [Test]
    public void ParseInputTextOscWithBelTerminatorIsFiltered()
    {
        // OSC may terminate with BEL instead of ST.
        var events = new List<InputEvent>(
            InputReplayFile.ParseInputText("m\x1b]11;rgb:0101/0404/0909\x07n", 0.0)
        );
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("m");
        events[1].Key.ShouldBe("n");
    }

    [Test]
    public void ParseInputTextLonePrivatePrefixCsiIsFiltered()
    {
        // ESC[<0;35;1M — xterm mouse event with '<' private prefix — should be filtered.
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("a\x1b[<0;35;1Mb", 0.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("a");
        events[1].Key.ShouldBe("b");
    }

    [Test]
    public void ParseInputTextIntermediateByteOnlyIsFiltered()
    {
        // A CSI sequence with intermediate byte but no private prefix: ESC[0\"q (DECSCA)
        var events = new List<InputEvent>(InputReplayFile.ParseInputText("a\x1b[0\"qb", 0.0));
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
}
