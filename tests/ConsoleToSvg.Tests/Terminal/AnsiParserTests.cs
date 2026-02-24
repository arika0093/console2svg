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
}
