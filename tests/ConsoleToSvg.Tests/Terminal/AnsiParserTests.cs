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

        first.Character.ShouldBe('A');
        first.Foreground.ShouldBe(theme.AnsiPalette[1]);
        second.Character.ShouldBe('B');
        second.Foreground.ShouldBe(theme.Foreground);
    }

    [Test]
    public void MoveCursorAndOverwrite()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("ABC\u001b[1D!");

        emulator.Buffer.GetCell(0, 0).Character.ShouldBe('A');
        emulator.Buffer.GetCell(0, 1).Character.ShouldBe('B');
        emulator.Buffer.GetCell(0, 2).Character.ShouldBe('!');
    }
}
