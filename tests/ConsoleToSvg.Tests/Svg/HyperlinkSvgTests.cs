using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Svg;

public sealed class HyperlinkSvgTests
{
    private static string Esc(string body) => "\u001b" + body;
    private static string St => "\u001b\\";
    private static string Bel => "\a";

    private static RecordingSession LinkedSession(string data)
    {
        var session = new RecordingSession(30, 2);
        session.AddEvent(0.01, data);
        return session;
    }

    [Test]
    public void LinkedCellsCarryHyperlinkUntilClosed()
    {
        var emulator = new TerminalEmulator(20, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("]8;;https://example.com") + St + "Link" + Esc("]8;;") + St + " after");

        emulator.Buffer.GetCell(0, 0).Hyperlink.ShouldBe("https://example.com");
        emulator.Buffer.GetCell(0, 3).Hyperlink.ShouldBe("https://example.com");
        emulator.Buffer.GetCell(0, 4).Hyperlink.ShouldBeNull();
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("L");
    }

    [Test]
    public void BelTerminatedHyperlinkIsRecognized()
    {
        var emulator = new TerminalEmulator(20, 2, Theme.Resolve("dark"));

        emulator.Process(Esc("]8;;https://example.com") + Bel + "L");

        emulator.Buffer.GetCell(0, 0).Hyperlink.ShouldBe("https://example.com");
    }

    [Test]
    public void HyperlinkSurvivesChunkBoundaries()
    {
        var link = Esc("]8;;https://example.com") + St;
        var full = link + "Link" + Esc("]8;;") + St;
        for (var split = 1; split < full.Length; split++)
        {
            var emulator = new TerminalEmulator(20, 2, Theme.Resolve("dark"));
            emulator.Process(full.Substring(0, split));
            emulator.Process(full.Substring(split));
            emulator.Buffer.GetCell(0, 0).Hyperlink.ShouldBe("https://example.com");
            emulator.Buffer.GetCell(0, 0).Text.ShouldBe("L");
        }
    }

    [Test]
    public void LinkedTextRendersClickableAnchor()
    {
        var svg = SvgRenderer.Render(LinkedSession(Esc("]8;;https://example.com") + St + "Link" + Esc("]8;;") + St + " after"), new SvgRenderOptions());

        svg.ShouldContain("<a href=\"https://example.com\">");
        svg.ShouldContain("Link");
    }

    [Test]
    public void AdjacentLinksAreNotMerged()
    {
        var data = Esc("]8;;https://a.example") + St + "AA" + Esc("]8;;https://b.example") + St + "BB" + Esc("]8;;") + St;
        var svg = SvgRenderer.Render(LinkedSession(data), new SvgRenderOptions());

        svg.ShouldContain("<a href=\"https://a.example\">");
        svg.ShouldContain("<a href=\"https://b.example\">");
    }

    [Test]
    public void UnsafeSchemesAreDropped()
    {
        var emulator = new TerminalEmulator(20, 2, Theme.Resolve("dark"));
        emulator.Process(Esc("]8;;javascript:alert(1)") + St + "L");
        emulator.Buffer.GetCell(0, 0).Hyperlink.ShouldBeNull();

        var svg = SvgRenderer.Render(LinkedSession(Esc("]8;;javascript:alert(1)") + St + "L"), new SvgRenderOptions());
        svg.ShouldNotContain("javascript:");
        svg.ShouldNotContain("<a href");

        var mail = SvgRenderer.Render(LinkedSession(Esc("]8;;mailto:foo@example.com") + St + "M"), new SvgRenderOptions());
        mail.ShouldNotContain("<a href");
    }

    [Test]
    public void HrefAttributeIsEscaped()
    {
        var svg = SvgRenderer.Render(LinkedSession(Esc("]8;;https://example.com/?a=1&b=2") + St + "L" + Esc("]8;;") + St), new SvgRenderOptions());

        svg.ShouldContain("https://example.com/?a=1&amp;b=2");
    }

    [Test]
    public void OverwriteClearsHyperlink()
    {
        var emulator = new TerminalEmulator(10, 2, Theme.Resolve("dark"));
        emulator.Process(Esc("]8;;https://example.com") + St + "AB" + Esc("]8;;") + St);
        emulator.Process(((char)13).ToString() + "XX");

        emulator.Buffer.GetCell(0, 0).Hyperlink.ShouldBeNull();
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
    }

    [Test]
    public void AnimatedSvgKeepsHyperlink()
    {
        var session = new RecordingSession(30, 2);
        session.AddEvent(0.01, Esc("]8;;https://example.com") + St + "Top" + Esc("]8;;") + St);
        session.AddEvent(0.02, "x");

        var svg = AnimatedSvgRenderer.Render(session, new SvgRenderOptions());

        svg.ShouldContain("https://example.com");
    }
}