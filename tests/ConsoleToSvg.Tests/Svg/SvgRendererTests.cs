using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed class SvgRendererTests
{
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
        svg.ShouldContain("viewBox=\"0 0 72 36\"");
        svg.ShouldContain(">H<");
        svg.ShouldContain(">i<");
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

        svg.ShouldContain("viewBox=\"0 0 63 18\"");
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
        svg.ShouldContain("viewBox=\"0 0 72 108\"");
        // All six lines should appear in the SVG
        svg.ShouldContain(">l<");
        svg.ShouldContain(">1<");
        svg.ShouldContain(">6<");
    }

    [Test]
    public void RenderStaticSvgWithSpecificFrameDoesNotIncludeScrollback()
    {
        // 2-row terminal with 3 lines of output (forces 1 row to scroll off)
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "line1\r\nline2\r\nline3");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Frame = 0,
            }
        );

        // When a specific frame is requested, only the terminal viewport (2 rows) is shown
        svg.ShouldContain("viewBox=\"0 0 72 36\"");
    }

    [Test]
    public void RenderStaticSvgWithCjkWideCharacters()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // CJK character 中 (U+4E2D) is wide (2 columns)
        session.AddEvent(0.01, "\u4e2d\u6587");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain(">\u4e2d<");
        svg.ShouldContain(">\u6587<");
        // The second CJK character starts at x=18 (2 cells from the first)
        svg.ShouldContain("x=\"18\"");
    }

    [Test]
    public void RenderStaticSvgWithEmoji()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // 😀 is U+1F600, a supplementary character (surrogate pair in UTF-16)
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
        svg.ShouldContain("viewBox=\"0 0 72 36\"");
        svg.ShouldContain(">l<");
        svg.ShouldContain(">-<");
        // "line3" and "line4" should NOT be in the output
        svg.ShouldNotContain(">3<");
        svg.ShouldNotContain(">4<");
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
        svg.ShouldContain("viewBox=\"0 0 72 54\"");
        svg.ShouldContain(">-<");
        svg.ShouldContain(">k<");
        // "skip" row should NOT be in the output
        svg.ShouldNotContain(">s<");
    }
}
