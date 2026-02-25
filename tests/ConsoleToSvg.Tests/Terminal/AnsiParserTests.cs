using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Terminal;

public sealed class AnsiParserTests
{
    [Test]
    public void ApplySgrColorAndReset()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b[31mA\u001b[0mB");

        var first = emulator.Buffer.GetCell(0, 0);
        var second = emulator.Buffer.GetCell(0, 1);

        first.Text.ShouldBe("A");
        first.Foreground.ShouldBe(theme.AnsiPalette[1]);
        second.Text.ShouldBe("B");
        second.Foreground.ShouldBe(theme.Foreground);
    }

    [Test]
    public void MoveCursorAndOverwrite()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("ABC\u001b[1D!");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("!");
    }

    [Test]
    public void CjkWideCharacterOccupiesTwoColumns()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 中 (U+4E2D) is a CJK wide character
        emulator.Process("\u4e2d");

        var first = emulator.Buffer.GetCell(0, 0);
        var second = emulator.Buffer.GetCell(0, 1);

        first.Text.ShouldBe("\u4e2d");
        first.IsWide.ShouldBeTrue();
        second.IsWideContinuation.ShouldBeTrue();
        emulator.Buffer.CursorCol.ShouldBe(2);
    }

    [Test]
    public void EmojiSurrogatePairStoredAsCluster()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 😀 is U+1F600, encoded as surrogate pair in UTF-16
        emulator.Process("\U0001F600");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("\U0001F600");
        cell.IsWide.ShouldBeTrue();
    }

    [Test]
    public void TrueColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 38;2;255;128;0 = orange RGB foreground
        emulator.Process("\u001b[38;2;255;128;0mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Foreground.ShouldBe("#FF8000");
    }

    [Test]
    public void ScrollbackBufferPreservesScrolledRows()
    {
        var theme = Theme.Resolve("dark");
        // 2-row terminal; write 3 lines to force scrolling
        var emulator = new TerminalEmulator(4, 2, theme);

        emulator.Process("AA\r\nBB\r\nCC");

        // "AA" should have scrolled off into the scrollback
        emulator.Buffer.ScrollbackCount.ShouldBe(1);
        emulator.Buffer.GetScrollbackCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetScrollbackCell(0, 1).Text.ShouldBe("A");

        // Current screen should show BB and CC
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("B");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("C");
    }

    [Test]
    public void ReverseVideoSwapsForegroundAndBackground()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 7 enables reverse video; SGR 27 disables it
        emulator.Process("\u001b[7mA\u001b[27mB");

        var reversed = emulator.Buffer.GetCell(0, 0);
        var normal = emulator.Buffer.GetCell(0, 1);

        reversed.Text.ShouldBe("A");
        reversed.Reversed.ShouldBeTrue();

        normal.Text.ShouldBe("B");
        normal.Reversed.ShouldBeFalse();
    }

    [Test]
    public void ReverseVideoResetByFullReset()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 7 sets reverse, SGR 0 resets all attributes including reverse
        emulator.Process("\u001b[7mA\u001b[0mB");

        emulator.Buffer.GetCell(0, 0).Reversed.ShouldBeTrue();
        emulator.Buffer.GetCell(0, 1).Reversed.ShouldBeFalse();
    }

    [Test]
    public void CombiningMarkAppendedToPreviousCell()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 'e' followed by combining acute accent (U+0301) should be stored as "é" in one cell
        emulator.Process("e\u0301");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("e\u0301");
        // Cursor should still be at column 1 (combining mark doesn't advance)
        emulator.Buffer.CursorCol.ShouldBe(1);
    }

    [Test]
    public void ZeroWidthCharsAreSkipped()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Zero-width space and ZWJ should not advance the cursor or place cells
        emulator.Process("A\u200BB");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.CursorCol.ShouldBe(2);
    }

    [Test]
    public void Ansi256ColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 38;5;196 = 256-color foreground (red in 6x6x6 cube)
        emulator.Process("\u001b[38;5;196mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        // 256-color index 196 = cube index 180 (r=5,g=0,b=0) = #FF0000
        cell.Foreground.ShouldBe("#FF0000");
    }
}
