using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Terminal;

public sealed class ControlStringTests
{
    private static string Esc(string body) => "\u001b" + body;
    private static string St => "\u001b\\";
    private static string Bel => "\a";

    private static string RowText(ScreenBuffer buffer, int row)
    {
        var text = "";
        for (var col = 0; col < buffer.Width; col++)
        {
            text += buffer.GetCell(row, col).Text;
        }
        return text;
    }

    [Test]
    public void ApcPayloadIsNotRendered()
    {
        var emulator = new TerminalEmulator(16, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("_Gi=1;AAAA") + St + "X");

        RowText(emulator.Buffer, 0).ShouldBe("X               ");
    }

    [Test]
    public void PmPayloadIsNotRendered()
    {
        var emulator = new TerminalEmulator(16, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("^1;payload") + St + "Y");

        RowText(emulator.Buffer, 0).ShouldBe("Y               ");
    }

    [Test]
    public void SosPayloadIsNotRendered()
    {
        var emulator = new TerminalEmulator(16, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("Xpayload;:,./?@+=-%_~!") + St + "Z");

        RowText(emulator.Buffer, 0).ShouldBe("Z               ");
    }

    [Test]
    public void IncompleteControlStringSurvivesChunkBoundary()
    {
        var emulator = new TerminalEmulator(16, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("_Gi=1;AAA"));
        RowText(emulator.Buffer, 0).ShouldBe("                ");

        emulator.Process("A" + St + "X");
        RowText(emulator.Buffer, 0).ShouldBe("X               ");
    }

    [Test]
    public void ControlStringsAreChunkEquivalent()
    {
        var bodies = new[] { "_Gi=1;AAAA", "^1;payload", "Xpayload", "P+q436f", "]0;title" };
        foreach (var body in bodies)
        {
            var terminator = body.StartsWith((char)93) ? Bel : St;
            var full = Esc(body) + terminator + "Hi";
            var reference = new TerminalEmulator(16, 2, Theme.Resolve("dark"));
            reference.Process(full);
            var want = RowText(reference.Buffer, 0);
            for (var split = 1; split < full.Length; split++)
            {
                var emulator = new TerminalEmulator(16, 2, Theme.Resolve("dark"));
                emulator.Process(full.Substring(0, split));
                emulator.Process(full.Substring(split));
                RowText(emulator.Buffer, 0).ShouldBe(want);
            }
        }
    }
}