using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed partial class SvgRendererTests
{
    [Test]
    public void RenderStaticSvgWithWindowsStyleHasTabAndTextButtons()
    {
        var session = new RecordingSession(width: 10, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Chrome = ConsoleToSvg.Svg.ChromeLoader.Load("windows"),
                Padding = 2,
            }
        );

        // Windows Terminal style: tab shape and vector control buttons
        svg.ShouldContain("fill=\"#333\""); // tab bar / outer fill
        svg.ShouldContain("stroke=\"#cccccc\""); // window control icons
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize 笆｡
        svg.ShouldContain("stroke-width=\"1.3\""); // close ﾃ・lines
    }

    [Test]
    public void RenderStaticSvgWithOpacityAppliedToBackground()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Opacity = 0.5d }
        );

        // Content should be wrapped in a <g opacity> group
        svg.ShouldContain("opacity=\"0.5\"");
    }

    [Test]
    public void RenderStaticSvgWithFullOpacityDoesNotAddFillOpacity()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Opacity = 1d }
        );

        // Default opacity=1 should not add fill-opacity attribute
        svg.ShouldNotContain("fill-opacity");
    }

    [Test]
    public void CommandHeaderDoesNotHideFirstTerminalRow()
    {
        var session = new RecordingSession(width: 20, height: 3);
        session.AddEvent(0.01, "row0\r\nrow1\r\nrow2");

        // With CommandHeader, the terminal content should show all rows starting from row 0
        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", CommandHeader = "$ ls" }
        );

        // All three terminal rows should be visible (not shifted/hidden by command header)
        svg.ShouldContain("row0");
        svg.ShouldContain("row1");
        svg.ShouldContain("row2");
        // The command header should also be present
        svg.ShouldContain("$ ls");
    }

    [Test]
    public void RenderWithSizeWidthOnlyScalesProportionally()
    {
        // Natural canvas: width=8 cols * 8.4 = 67.2, height=2 rows * 18 = 36
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeWidth = 134.4d, // 2x natural width
            }
        );

        // SVG width should be the target; height scales proportionally (2x = 72)
        svg.ShouldContain("width=\"134.4\"");
        svg.ShouldContain("height=\"72\"");
        // viewBox stays at natural canvas dimensions
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
        svg.ShouldContain(">Hi<");
    }

    [Test]
    public void RenderWithSizeHeightOnlyScalesProportionally()
    {
        // Natural canvas: width=67.2, height=36
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeHeight = 72d, // 2x natural height
            }
        );

        // SVG height should be the target; width scales proportionally (2x = 134.4)
        svg.ShouldContain("width=\"134.4\"");
        svg.ShouldContain("height=\"72\"");
        // viewBox stays at natural canvas dimensions
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
    }

    [Test]
    public void RenderWithSizeBothSameAspectRatioScalesProportionally()
    {
        // Natural canvas: width=8 cols * 8.4 = 67.2, height=2 rows * 18 = 36 (aspect ratio ~1.867)
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeWidth = 134.4d,
                SizeHeight = 72d, // same aspect ratio as natural
            }
        );

        // SVG dimensions should match the specified size
        svg.ShouldContain("width=\"134.4\"");
        svg.ShouldContain("height=\"72\"");
        // viewBox has the natural canvas dimensions (content fills the entire area, no centering needed)
        svg.ShouldContain("viewBox=\"0 0");
        svg.ShouldContain(">Hi<");
    }

    [Test]
    public void RenderWithSizeBothDifferentAspectRatioExtendsBackground()
    {
        // Natural canvas: width=67.2, height=36; aspect ratio ~1.867
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        // Use 200x200 (1:1 aspect ratio) – content fills width (scale=200/67.2≈2.976),
        // with top/bottom margins to center the content (scaledH ≈ 107 < 200)
        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeWidth = 200d,
                SizeHeight = 200d,
                Background = new[] { "#ff0000" },
            }
        );

        // Output dimensions should be the requested size
        svg.ShouldContain("width=\"200\"");
        svg.ShouldContain("height=\"200\"");
        // The viewBox Y origin should be negative (content is centered vertically in a taller canvas)
        svg.ShouldContain("viewBox=\"0 -");
        // Background rect should extend to the full viewBox area
        svg.ShouldContain("fill=\"#ff0000\"");
        // Content is still present
        svg.ShouldContain(">Hi<");
    }

    [Test]
    public void RenderWithImageBackgroundUsesViewBoxBoundsForCoverCenterLayout()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeWidth = 200d,
                SizeHeight = 200d,
                Background = new[] { "background.png" },
            }
        );

        svg.ShouldContain("viewBox=\"0 -15.6 67.2 67.2\"");
        svg.ShouldContain(
            "<pattern id=\"desktop-bg\" patternUnits=\"userSpaceOnUse\" patternContentUnits=\"userSpaceOnUse\" x=\"0\" y=\"-15.6\" width=\"67.2\" height=\"67.2\">"
        );
        svg.ShouldContain(
            "<image href=\"background.png\" x=\"0\" y=\"0\" width=\"67.2\" height=\"67.2\" preserveAspectRatio=\"xMidYMid slice\"/>"
        );
    }

    [Test]
    public void RenderWithImageBackgroundDoesNotDoubleApplyHorizontalViewBoxOffset()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                SizeWidth = 200d,
                SizeHeight = 100d,
                Background = new[] { "background.png" },
            }
        );

        svg.ShouldContain("viewBox=\"-2.4 0 72 36\"");
        svg.ShouldContain(
            "<pattern id=\"desktop-bg\" patternUnits=\"userSpaceOnUse\" patternContentUnits=\"userSpaceOnUse\" x=\"-2.4\" y=\"0\" width=\"72\" height=\"36\">"
        );
        svg.ShouldContain(
            "<image href=\"background.png\" x=\"0\" y=\"0\" width=\"72\" height=\"36\" preserveAspectRatio=\"xMidYMid slice\"/>"
        );
    }
}
