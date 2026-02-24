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
}
