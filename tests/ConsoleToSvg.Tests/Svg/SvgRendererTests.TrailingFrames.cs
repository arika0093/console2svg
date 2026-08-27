using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed partial class SvgRendererTests
{
    [Test]
    public void RenderStaticSvgDropsTrailingBlankFrameFromAlternateScreenLeave()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "\u001b[?1049hHELLO");
        session.AddEvent(0.2, "\u001b[?1049l");

        var defaultSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );
        var previousFrameSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Frame = 0 }
        );
        var lastFrameSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Frame = 1 }
        );

        defaultSvg.ShouldContain("HELLO");
        defaultSvg.ShouldBe(previousFrameSvg);
        defaultSvg.ShouldNotBe(lastFrameSvg);
    }

    [Test]
    public void RenderStaticSvgDropsTrailingBlankFrameFromClearHomeTail()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "HELLO");
        session.AddEvent(0.2, "\u001b[2J\u001b[H");

        var defaultSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );
        var previousFrameSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Frame = 0 }
        );
        var lastFrameSvg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Frame = 1 }
        );

        defaultSvg.ShouldContain("HELLO");
        defaultSvg.ShouldBe(previousFrameSvg);
        defaultSvg.ShouldNotBe(lastFrameSvg);
    }

    [Test]
    public void RenderStaticSvgWithMacosWindowRendersTrafficLights()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Chrome = ConsoleToSvg.Svg.ChromeLoader.Load("macos"),
                Padding = 2,
            }
        );

        svg.ShouldContain("#ff5f57");
        svg.ShouldContain("#febc2e");
        svg.ShouldContain("#28c840");
    }

    [Test]
    public void RenderStaticSvgWithMacosPcWindowRendersDesktopAndTrafficLights()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Chrome = ConsoleToSvg.Svg.ChromeLoader.Load("macos-pc"),
                Padding = 2,
            }
        );

        // Desktop background - now uses a gradient
        svg.ShouldContain("linearGradient");
        svg.ShouldContain("#1a1d2e"); // gradient start
        svg.ShouldContain("#252840"); // gradient end
        // No drop shadow
        svg.ShouldNotContain("fill-opacity=\"0.3\"");
        svg.ShouldContain("#ff5f57");
        svg.ShouldContain("#febc2e");
        svg.ShouldContain("#28c840");
    }

    [Test]
    public void RenderStaticSvgWithWindowsPcWindowRendersDesktopAndControls()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Chrome = ConsoleToSvg.Svg.ChromeLoader.Load("windows-pc"),
                Padding = 2,
            }
        );

        // Desktop background - now uses a gradient
        svg.ShouldContain("linearGradient");
        svg.ShouldContain("#1a2535"); // gradient start
        svg.ShouldContain("#253345"); // gradient end
        // Shadow (black with opacity)
        svg.ShouldContain("fill-opacity=\"0.25\"");
        // Windows Terminal style: control buttons as vector lines/rects
        svg.ShouldContain("stroke=\"#cccccc\""); // icon stroke color
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize rect
        svg.ShouldContain("stroke-width=\"1.3\""); // close lines
        // Active tab shape present
        svg.ShouldContain("fill=\"#333\"");
    }

    [Test]
    public void RenderStaticSvgWithHeightLimitCapsRows()
    {
        // 4-row terminal with 6 lines of output (forces 2 rows to scroll off)
        var session = new RecordingSession(width: 8, height: 4);
        session.AddEvent(0.01, "line1\r\nline2\r\nline3\r\nline4\r\nline5\r\nline6");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", HeightRows = 3 }
        );

        // Only 3 rows should be visible: height = 3 * 18 = 54, width = 8 * 8.4 = 67.2
        svg.ShouldContain("viewBox=\"0 0 67.2 54\"");
    }

    [Test]
    public void WindowAndPaddingParsedForNewStyles()
    {
        var macOptions = new ConsoleToSvg.Cli.AppOptions { Window = "macos-pc" };
        var windowsOptions = new ConsoleToSvg.Cli.AppOptions { Window = "windows-pc" };

        ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(macOptions).Chrome.ShouldNotBeNull();
        ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(windowsOptions).Chrome.ShouldNotBeNull();
    }

    [Test]
    public void WithCommandPrependsPromptLineToSession()
    {
        // Simulate what Program.cs does when --with-command is set
        var session = new RecordingSession(width: 20, height: 4);
        session.Events.Insert(
            0,
            new ConsoleToSvg.Recording.AsciicastEvent
            {
                Time = 0.0,
                Type = "o",
                Data = "$ ls\r\n",
            }
        );
        session.AddEvent(0.1, "file1.txt  file2.txt");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The prompt and command should appear in the SVG, merged into a single
        // <text> run with the internal space preserved.
        svg.ShouldContain(">$ ls<");
    }

    [Test]
    public void CommandHeaderRenderedAboveContentAndNotAffectedByCropTop()
    {
        var session = new RecordingSession(width: 20, height: 4);
        session.AddEvent(0.01, "header-row\r\ncontent-row");

        // CommandHeader is always shown; crop-top removes rows from the session content
        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("1ch", "0", "0", "0"),
                CommandHeader = "$ ls",
            }
        );

        // Command header text should always appear in the SVG regardless of crop
        svg.ShouldContain("$ ls");
    }

    [Test]
    public void DefaultPaddingIsEightWhenWindowIsSet()
    {
        var options = new ConsoleToSvg.Cli.AppOptions { Window = "macos" };
        var renderOptions = ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(options);
        renderOptions.Padding.ShouldBe(8d);
    }

    [Test]
    public void ExplicitPaddingOverridesWindowDefault()
    {
        var options = new ConsoleToSvg.Cli.AppOptions { Window = "macos", Padding = 3 };
        var renderOptions = ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(options);
        renderOptions.Padding.ShouldBe(3d);
    }

    [Test]
    public void HeaderOverridesCommandInRenderOptions()
    {
        var options = new ConsoleToSvg.Cli.AppOptions
        {
            WithCommand = true,
            Command = "ls",
            Header = "custom",
            Prompt = "@",
        };
        var renderOptions = ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(options);
        renderOptions.CommandHeader.ShouldBe("@ custom");
    }

    [Test]
    public void PromptOverridesCommandPrefixInRenderOptions()
    {
        var options = new ConsoleToSvg.Cli.AppOptions
        {
            WithCommand = true,
            Command = "ls",
            Prompt = "#",
        };
        var renderOptions = ConsoleToSvg.Cli.SvgRenderOptionsFactory.Create(options);
        renderOptions.CommandHeader.ShouldBe("# ls");
    }

    [Test]
    public void HeightPreservedWhenCropReducesBelowSpecifiedHeight()
    {
        // 4-row terminal with content only in first 2 rows
        var session = new RecordingSession(width: 8, height: 4);
        session.AddEvent(0.01, "line1\r\nline2");

        // With --height=4 and --crop-bottom=3ch, only 1 row would normally be visible
        // but -h should preserve the 4-row height
        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                HeightRows = 4,
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("0", "0", "3ch", "0"),
            }
        );

        // Canvas height should be at least 4 * 18 = 72 pixels (not reduced by crop)
        svg.ShouldContain("viewBox=\"0 0 67.2 72\"");
    }

    [Test]
    public void WindowsPcButtonsAreInsideWindow()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Chrome = ConsoleToSvg.Svg.ChromeLoader.Load("windows-pc"),
                Padding = 8,
            }
        );

        // Windows Terminal style: control buttons as vector lines/rects, inside the window.
        // Active tab uses theme.Background (same as content area)
        svg.ShouldContain("stroke=\"#cccccc\""); // window control buttons
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize 笆｡
        svg.ShouldContain("stroke-width=\"1.3\""); // close ﾃ・lines

        // The desktop background uses a gradient
        svg.ShouldContain("linearGradient");
    }
}
