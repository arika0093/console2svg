using System.IO;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed partial class SvgRendererTests
{
    [Test]
    public void WriteMatchesStringRender()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "\u001b[32mHELLO");
        var options = new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" };
        using var writer = new StringWriter();

        ConsoleToSvg.Svg.SvgRenderer.Write(writer, session, options);

        writer.ToString().ShouldBe(ConsoleToSvg.Svg.SvgRenderer.Render(session, options));
    }

    [Test]
    public void RenderStaticSvgFromLastFrame()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("0", "0", "0", "0"),
            }
        );

        svg.ShouldContain("<svg");
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
        svg.ShouldContain(">Hi<");
    }

    [Test]
    public void RenderStaticSvgIncludesCursorAtTerminalPosition()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<rect class=\"u\" x=\"16.8\" width=\"8.4\" height=\"18\"");
    }

    [Test]
    public void RenderStaticSvgOmitsHiddenCursor()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi\u001b[?25l");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldNotContain("class=\"u\"");
    }

    [Test]
    public void RenderStaticSvgOmitsZeroRectCoordinates()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, " █\r\n█");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<rect class=\"q\" x=\"8.4\" width=\"8.4\" height=\"18\"");
        svg.ShouldContain("<rect class=\"q\" y=\"18\" width=\"8.4\" height=\"18\"");
    }

    [Test]
    public void RenderStaticSvgMergesWhitespaceSeparatedWordsIntoSingleTextNode()
    {
        var session = new RecordingSession(width: 30, height: 3);
        session.AddEvent(0.01, "VIM - Vi Improved\r\nby Bram Moolenaar et al.");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">VIM - Vi Improved<");
        svg.ShouldContain(">by Bram Moolenaar et al.<");
        svg.ShouldContain(".c2 .w { white-space: pre; }");
        svg.ShouldContain(" w\"");
        svg.ShouldNotContain("xml:space=");
    }

    [Test]
    public void RenderStaticSvgMergesMultipleSpacesAndTabsIntoOneNode()
    {
        var session = new RecordingSession(width: 40, height: 2);
        session.AddEvent(0.01, "This   is    a\t\tMessage");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // Tabs expand to spaces (2 + 8 = 10 cells) in the terminal; the whole
        // run merges into a single <text> node with preserved whitespace.
        var expected = "This" + new string(' ', 3) + "is" + new string(' ', 4)
            + "a" + new string(' ', 10) + "Message";
        svg.ShouldContain($">{expected}<");
        svg.ShouldContain(" w\"");
        svg.ShouldNotContain("xml:space=");
    }

    [Test]
    public void RenderStaticSvgWithCharacterCrop()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hello");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("1ch", "1ch", "0", "0"),
            }
        );

        svg.ShouldContain("viewBox=\"0 0 58.8 18\"");
    }

    [Test]
    public void RenderStaticSvgIncludesScrollbackWhenOutputIsLong()
    {
        // 4-row terminal with 6 lines of output (forces 2 rows to scroll off)
        var session = new RecordingSession(width: 8, height: 4);
        session.AddEvent(0.01, "line1\r\nline2\r\nline3\r\nline4\r\nline5\r\nline6");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The SVG should include all 6 rows (4 screen + 2 scrollback)
        svg.ShouldContain("viewBox=\"0 0 67.2 108\"");
        // All six lines should appear in the SVG
        svg.ShouldContain("line1");
        svg.ShouldContain("line6");
    }

    [Test]
    public void RenderStaticSvgWithSpecificFrameDoesNotIncludeScrollback()
    {
        // 2-row terminal with 3 lines of output (forces 1 row to scroll off)
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "line1\r\nline2\r\nline3");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Frame = 0 }
        );

        // When a specific frame is requested, only the terminal viewport (2 rows) is shown
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
    }

    [Test]
    public void RenderStaticSvgWithCjkWideCharacters()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // CJK character 荳ｭ (U+4E2D) is wide (2 columns)
        session.AddEvent(0.01, "\u4e2d\u6587");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">\u4e2d<");
        svg.ShouldContain(">\u6587<");
        // The second CJK character starts at x=16.8 (2 cells * 8.4px from the first)
        svg.ShouldContain("x=\"16.8\"");
    }

    [Test]
    public void RenderStaticSvgWithBoxDrawingUsesConnectedPaths()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u001b(0lqqkxmqqj\u001b(B");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );
        svg.ShouldContain("<path d=\"");
        svg.ShouldContain("<path d=\"");
        svg.ShouldNotContain("shape-rendering=\"crispEdges\"");
        svg.ShouldNotContain(">┌");
        svg.ShouldNotContain(">─");
    }

    [Test]
    public void RenderStaticSvgWithUnicodeBoxDrawingUsesConnectedPaths()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "└──┘");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<path d=\"");
        // Horizontal line segments are merged into a single path
        svg.ShouldContain("M4.2 8.5H29.4V9.5H4.2Z");
        // Vertical line segments are also present
        svg.ShouldContain("M3.7 0H4.7V9H3.7Z");
        svg.ShouldNotContain("id=\"c2e");
        svg.ShouldNotContain(">└");
        svg.ShouldNotContain(">─");
        svg.ShouldNotContain(">┘");
    }

    [Test]
    public void RenderStaticSvgWithRepeatedBoxDrawingUsesConnectedPaths()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u001b(0lq\u001b[5bk\u001b(B");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<path d=\"");
        svg.ShouldNotContain(">q<");
        svg.ShouldNotContain(">─<");
    }

    [Test]
    public void RenderStaticSvgWithRoundedBoxDrawingUsesCalibratedPaths()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "╭─╮\r\n╰─╯");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<path d=\"M8.4 9Q4.2 9 4.2 18\"");
        svg.ShouldContain("<path d=\"M16.8 9Q21 9 21 18\"");
        svg.ShouldNotContain(">╭");
        svg.ShouldNotContain(">╮");
        svg.ShouldNotContain(">╰");
        svg.ShouldNotContain(">╯");
    }

    [Test]
    public void RenderStaticSvgSupportsSgrDecorations()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u001b[4;9;53;58;2;1;2;3mA\u001b[8mB");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("text-decoration:underline line-through overline");
        svg.ShouldContain("text-decoration-color:#010203");
        svg.ShouldContain(">A<");
        svg.ShouldNotContain(">B<");
    }

    [Test]
    public void RenderStaticSvgWithEmoji()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // ・ is U+1F600, a supplementary character (surrogate pair in UTF-16)
        session.AddEvent(0.01, "\U0001F600");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">\U0001F600<");
    }

    [Test]
    public void RenderStaticSvgWithTrueColor()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // SGR 38;2;255;128;0 sets foreground to orange (true color)
        session.AddEvent(0.01, "\u001b[38;2;255;128;0mA\u001b[0m");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("#FF8000");
        svg.ShouldContain(">A<");
    }

    [Test]
    public void RenderStaticSvgWithReverseVideoSwapsFgAndBg()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // SGR 7 = reverse video: fg becomes bg and bg becomes fg
        session.AddEvent(0.01, "\u001b[7mA\u001b[0m");

        var theme = ConsoleToSvg.Terminal.Theme.Resolve("dark");
        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The text fill should use the theme background (swapped fg)
        svg.ShouldContain($"fill=\"{theme.Background}\"");
        // A background rect should be drawn with the theme foreground color (swapped bg)
        svg.ShouldContain($"fill=\"{theme.Foreground}\"");
        svg.ShouldContain(">A<");
    }

    [Test]
    public void RenderStaticSvgWithTextCropBottom()
    {
        // 4-row terminal: row 0 = "line1", row 1 = "---", row 2 = "line3", row 3 = "line4"
        var session = new RecordingSession(width: 8, height: 4);
        session.AddEvent(0.01, "line1\r\n---\r\nline3\r\nline4");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                // Crop from the bottom up to the row containing "---" (inclusive)
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("0", "0", "text:---", "0"),
            }
        );

        // Only 2 rows visible: "line1" (row 0) and "---" (row 1)
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
        svg.ShouldContain("line1");
        svg.ShouldContain(">---<");
        // "line3" and "line4" should NOT be in the output
        svg.ShouldNotContain("line3");
        svg.ShouldNotContain("line4");
    }

    [Test]
    public void RenderStaticSvgWithTextCropTop()
    {
        // 4-row terminal: row 0 = "skip", row 1 = "---", row 2 = "keep", row 3 = "more"
        var session = new RecordingSession(width: 8, height: 4);
        session.AddEvent(0.01, "skip\r\n---\r\nkeep\r\nmore");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                // Crop from top down to the row containing "---" (that row becomes first visible)
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("text:---", "0", "0", "0"),
            }
        );

        // 3 rows visible: "---" (row 1), "keep" (row 2), "more" (row 3)
        svg.ShouldContain("viewBox=\"0 0 67.2 54\"");
        svg.ShouldContain(">---<");
        svg.ShouldContain("keep");
        // "skip" row should NOT be in the output
        svg.ShouldNotContain("skip");
    }

    [Test]
    public void RenderStaticSvgWithBareTextCropTop()
    {
        var session = new RecordingSession(width: 12, height: 4);
        session.AddEvent(0.01, "before\r\nsummary\r\nafter\r\nend");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("summary", "0", "0", "0"),
            }
        );

        svg.ShouldContain("summary");
        svg.ShouldContain("after");
        svg.ShouldNotContain("before");
    }

    [Test]
    public void RenderStaticSvgUsesDefaultCompatibleMonospaceFont()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("DejaVu Sans Mono");
    }

    [Test]
    public void RenderStaticSvgWithCustomFont()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Font = "Consolas, monospace" }
        );

        svg.ShouldContain("Consolas, monospace");
        svg.ShouldNotContain("ui-monospace");
    }

    [Test]
    public void RenderStaticSvgFullWidthLineDoesNotProduceExtraBlankRow()
    {
        // 4-wide terminal; fill row 0 completely then CRLF 窶・should produce 2 content rows, not 3
        var session = new RecordingSession(width: 4, height: 3);
        session.AddEvent(0.01, "ABCD\r\nEF");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The terminal viewport is 3 rows ﾃ・4 cols = height 54, width 33.6
        svg.ShouldContain("viewBox=\"0 0 33.6 54\"");
        svg.ShouldContain(">ABCD<");
        svg.ShouldContain(">EF<");
    }

    [Test]
    public void RenderStaticSvgWithEmojiVariationSelector()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // 孱・・= U+1F6E1 (shield) + U+FE0F (variation selector-16 = emoji presentation)
        session.AddEvent(0.01, "\U0001F6E1\uFE0F");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The SVG should contain the full emoji with its variation selector
        svg.ShouldContain("\U0001F6E1\uFE0F");
    }

    [Test]
    public void RenderStaticSvgKeepsPipeAlignedAfterBmpEmojiVariationSelector()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u2705\uFE0F|");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">\u2705\uFE0F<");
        svg.ShouldContain("x=\"16.8\"");
        svg.ShouldContain(">|<");
    }

    [Test]
    public void RenderStaticSvgKeepsPipeAlignedAfterBmpEmojiWithoutVariationSelector()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u2705|");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">\u2705<");
        svg.ShouldContain("x=\"16.8\"");
        svg.ShouldContain(">|<");
    }

    [Test]
    public void RenderStaticSvgPreservesWhiteHighlightNearGreen()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u001b[32mA\u001b[37mB\u001b[39m");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("fill:#e5e5e5");
    }

    [Test]
    public void RenderStaticSvgWithPaddingExpandsViewBox()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Padding = 2 }
        );

        svg.ShouldContain("viewBox=\"0 0 71.2 40\"");
    }

    [Test]
    public void RenderStaticSvgOmitsDefaultLengthAdjustAndPreservesCustomValue()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "AB");

        var defaultSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );
        var customSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                LengthAdjust = "spacingAndGlyphs",
            }
        );

        defaultSvg.ShouldNotContain("lengthAdjust=");
        customSvg.ShouldContain("lengthAdjust=\"spacingAndGlyphs\"");
    }

    [Test]
    public void RenderStaticSvgHoistsFontAndDeduplicatesTextStyles()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A\r\nB");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );
        var foreground = ConsoleToSvg.Terminal.Theme.Resolve("dark").Foreground;

        svg.ShouldContain("<g class=\"c2 c\"");
        svg.ShouldNotContain("<text class=\"c");
        System.Text.RegularExpressions.Regex.IsMatch(svg, "<text[^>]* x=\"0\"")
            .ShouldBeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(svg, "<text[^>]+ fill=").ShouldBeFalse();
        CountOccurrences(svg, $"{{fill:{foreground}}}").ShouldBe(1);
        CountOccurrences(svg, "<style>").ShouldBe(1);
        svg.ShouldContain($"\n.c2 .a{{fill:{foreground}}}\n");
        svg.ShouldNotContain("transform=\"translate(0 0)\"");
        svg.IndexOf(".c2 .a{", System.StringComparison.Ordinal)
            .ShouldBeLessThan(svg.IndexOf("</style>", System.StringComparison.Ordinal));
        System.Xml.Linq.XDocument.Parse(svg).Root.ShouldNotBeNull();
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
