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

    [Test]
    public void ParseInputTextPartialOscPrefixAtEnd()
    {
        // ESC] at end → incomplete OSC
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("x\x1b]", 1.0);
        events.Count.ShouldBe(1); // x
        remainder.ShouldBe("\x1b]");
    }

    [Test]
    public void ParseInputTextPartialDcsPrefixAtEnd()
    {
        // ESCP at end → incomplete DCS
        var (events, remainder) = InputReplayFile.ParseInputTextPartial("x\x1bP", 1.0);
        events.Count.ShouldBe(1); // x
        remainder.ShouldBe("\x1bP");
    }

    [Test]
    public void ParseInputTextPartialDcsSplitAtStEscCarriesFromDcsStart()
    {
        // Chunk split right before ST final '\\':
        // first chunk ends with ESC (start of ST), second chunk starts with '\\'.
        // Remainder must start at ESCP, not that trailing ESC, otherwise Alt+P garbage appears.
        var chunk1 = "a\x1bP>|xterm.js(6.1.0-beta.109)\x1b";
        var (events1, rem1) = InputReplayFile.ParseInputTextPartial(chunk1, 1.0);
        events1.Count.ShouldBe(1);
        events1[0].Key.ShouldBe("a");
        rem1.ShouldBe("\x1bP>|xterm.js(6.1.0-beta.109)\x1b");

        var chunk2 = rem1 + "\\b";
        var (events2, rem2) = InputReplayFile.ParseInputTextPartial(chunk2, 1.1);
        events2.Count.ShouldBe(1);
        events2[0].Key.ShouldBe("b");
        rem2.ShouldBe("");
    }

    // ── Win32-input-mode VK=0 VT-passthrough tests ─────────────────────────
    //
    // Some terminals (e.g. copilot CLI) send VT escape sequences through
    // Win32-input-mode as individual character events with VK=0, SC=0.
    // For example, Shift+Tab (ESC[Z) arrives as three events:
    //   \x1b[0;0;27;1;0;1_  (UC=ESC)
    //   \x1b[0;0;91;1;0;1_  (UC='[')
    //   \x1b[0;0;90;1;0;1_  (UC='Z')
    // These must be reassembled and parsed as a single VT sequence.

    [Test]
    public void Win32Vk0Passthrough_ShiftTab_ThreeEvents()
    {
        // Exact pattern from console2svg.log: Shift+Tab as VK=0 char-by-char
        var text = "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;90;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("Tab");
        events[0].Modifiers.ShouldContain("shift");
    }

    [Test]
    public void Win32Vk0Passthrough_ArrowDown()
    {
        // ESC[B as VK=0 char-by-char: UC=27, UC=91('['), UC=66('B')
        var text = "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;66;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowDown");
    }

    [Test]
    public void Win32Vk0Passthrough_ArrowUp()
    {
        // ESC[A as VK=0 char-by-char: UC=27, UC=91('['), UC=65('A')
        var text = "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;65;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
    }

    [Test]
    public void Win32Vk0Passthrough_MixedWithRegularWin32Events()
    {
        // Exact sequence from the log:
        // Enter (VK=13), h (VK=72), e (VK=69), l (VK=76), l, o (VK=79),
        // then Shift+Tab as VK=0 passthrough, then ArrowDown as VK=0 passthrough
        var text =
            "\x1b[13;28;13;1;0;1_\x1b[13;28;13;0;0;1_"
            + // Enter (down+up)
            "\x1b[72;35;104;1;0;1_\x1b[72;35;104;0;0;1_"
            + // h (down+up)
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;90;1;0;1_"
            + // Shift+Tab (VK=0)
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;66;1;0;1_"; // ArrowDown (VK=0)
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(4);
        events[0].Key.ShouldBe("Enter");
        events[1].Key.ShouldBe("h");
        events[2].Key.ShouldBe("Tab");
        events[2].Modifiers.ShouldContain("shift");
        events[3].Key.ShouldBe("ArrowDown");
    }

    [Test]
    public void Win32Vk0Passthrough_FullLogScenario()
    {
        // Full scenario from the first run in the log:
        // Enter, h, e, l, l, o, ShiftTab(VK=0), ShiftTab(VK=0),
        // ArrowDown(VK=0), ArrowUp(VK=0), Enter, Ctrl+C, Ctrl+C
        var text =
            "\x1b[13;28;13;1;0;1_\x1b[13;28;13;0;0;1_"
            + // Enter
            "\x1b[72;35;104;1;0;1_\x1b[72;35;104;0;0;1_"
            + // h
            "\x1b[69;18;101;1;0;1_\x1b[69;18;101;0;0;1_"
            + // e
            "\x1b[76;38;108;1;0;1_\x1b[76;38;108;0;0;1_"
            + // l
            "\x1b[76;38;108;1;0;1_\x1b[76;38;108;0;0;1_"
            + // l
            "\x1b[79;24;111;1;0;1_\x1b[79;24;111;0;0;1_"
            + // o
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;90;1;0;1_"
            + // Shift+Tab
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;90;1;0;1_"
            + // Shift+Tab
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;66;1;0;1_"
            + // ArrowDown
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;65;1;0;1_"
            + // ArrowUp
            "\x1b[13;28;13;1;0;1_\x1b[13;28;13;0;0;1_"
            + // Enter
            "\x1b[67;0;3;1;8;1_"
            + // Ctrl+C
            "\x1b[67;0;3;1;8;1_"; // Ctrl+C
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(13);
        events[0].Key.ShouldBe("Enter");
        events[1].Key.ShouldBe("h");
        events[2].Key.ShouldBe("e");
        events[3].Key.ShouldBe("l");
        events[4].Key.ShouldBe("l");
        events[5].Key.ShouldBe("o");
        events[6].Key.ShouldBe("Tab");
        events[6].Modifiers.ShouldContain("shift");
        events[7].Key.ShouldBe("Tab");
        events[7].Modifiers.ShouldContain("shift");
        events[8].Key.ShouldBe("ArrowDown");
        events[9].Key.ShouldBe("ArrowUp");
        events[10].Key.ShouldBe("Enter");
        events[11].Key.ShouldBe("c");
        events[11].Modifiers.ShouldContain("ctrl");
        events[12].Key.ShouldBe("c");
        events[12].Modifiers.ShouldContain("ctrl");
    }

    [Test]
    public void Win32Vk0Passthrough_DoesNotAffectRegularWin32()
    {
        // Regular Win32 ArrowDown (VK=40) should still work normally
        var text = "\x1b[40;72;0;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowDown");
    }

    [Test]
    public void Win32Vk0Passthrough_NeverProducesLiteralBracket()
    {
        // The original bug: VK=0 passthrough was producing "[" + "Z" or "[" + "B"
        var text = "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;90;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.ShouldNotContain(e => e.Key == "[");
        events.ShouldNotContain(e => e.Key == "Z");
    }

    [Test]
    public void Win32Vk0Passthrough_Ss3ArrowUp()
    {
        // ESC O A (SS3 ArrowUp) as VK=0: UC=27, UC=79('O'), UC=65('A')
        var text = "\x1b[0;0;27;1;0;1_\x1b[0;0;79;1;0;1_\x1b[0;0;65;1;0;1_";
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(1);
        events[0].Key.ShouldBe("ArrowUp");
    }

    [Test]
    public void Win32Vk0Passthrough_ConsecutiveVtSequences()
    {
        // Two VT sequences in a row as VK=0: ESC[B + ESC[A
        var text =
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;66;1;0;1_"
            + // ArrowDown
            "\x1b[0;0;27;1;0;1_\x1b[0;0;91;1;0;1_\x1b[0;0;65;1;0;1_"; // ArrowUp
        var events = new List<InputEvent>(InputReplayFile.ParseInputText(text, 1.0));
        events.Count.ShouldBe(2);
        events[0].Key.ShouldBe("ArrowDown");
        events[1].Key.ShouldBe("ArrowUp");
    }
}
