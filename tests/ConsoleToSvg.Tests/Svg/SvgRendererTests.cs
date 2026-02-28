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
        svg.ShouldContain("viewBox=\"0 0 67.2 36\"");
        svg.ShouldContain(">Hi<");
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
        // CJK character 中 (U+4E2D) is wide (2 columns)
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
    public void RenderStaticSvgUsesDefaultSystemMonospaceFont()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("ui-monospace");
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
        // 4-wide terminal; fill row 0 completely then CRLF — should produce 2 content rows, not 3
        var session = new RecordingSession(width: 4, height: 3);
        session.AddEvent(0.01, "ABCD\r\nEF");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // The terminal viewport is 3 rows × 4 cols = height 54, width 33.6
        svg.ShouldContain("viewBox=\"0 0 33.6 54\"");
        svg.ShouldContain(">ABCD<");
        svg.ShouldContain(">EF<");
    }

    [Test]
    public void RenderStaticSvgWithEmojiVariationSelector()
    {
        var session = new RecordingSession(width: 8, height: 2);
        // 🛡️ = U+1F6E1 (shield) + U+FE0F (variation selector-16 = emoji presentation)
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
    public void RenderStaticSvgTintsWhiteHighlightNearGreenToBrightGreen()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "\u001b[32mA\u001b[37mB\u001b[39m");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("fill=\"#23D18B\"");
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
    public void RenderStaticSvgWithMacosWindowRendersTrafficLights()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Window = ConsoleToSvg.Svg.WindowStyle.Macos,
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
                Window = ConsoleToSvg.Svg.WindowStyle.MacosPc,
                Padding = 2,
            }
        );

        // Desktop background — now uses a gradient
        svg.ShouldContain("linearGradient");
        svg.ShouldContain("#1a1d2e"); // gradient start
        svg.ShouldContain("#252840"); // gradient end
        // Shadow (black with opacity)
        svg.ShouldContain("fill-opacity=\"0.3\"");
        // Traffic lights
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
                Window = ConsoleToSvg.Svg.WindowStyle.WindowsPc,
                Padding = 2,
            }
        );

        // Desktop background — now uses a gradient
        svg.ShouldContain("linearGradient");
        svg.ShouldContain("#1a2535"); // gradient start
        svg.ShouldContain("#253345"); // gradient end
        // Shadow (black with opacity)
        svg.ShouldContain("fill-opacity=\"0.25\"");
        // Windows Terminal style: control buttons as vector lines/rects
        svg.ShouldContain("stroke=\"#cccccc\""); // icon stroke color
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize □ rect
        svg.ShouldContain("stroke-width=\"1.3\""); // close × lines
        // Active tab shape present
        svg.ShouldContain("#1c1c1c");
        // Tab close ×, + button, | separator, v chevron all present
        svg.ShouldContain("stroke=\"#888888\"");
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
        var ok = ConsoleToSvg.Cli.OptionParser.TryParse(
            new[] { "--window", "macos-pc" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("macos-pc");

        ok = ConsoleToSvg.Cli.OptionParser.TryParse(
            new[] { "--window", "windows-pc" },
            out options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("windows-pc");
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

        // The prompt and command should appear in the SVG
        svg.ShouldContain(">$<");
        svg.ShouldContain(">ls<");
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
        var ok = ConsoleToSvg.Cli.OptionParser.TryParse(
            new[] { "--window", "macos" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var renderOptions = ConsoleToSvg.Svg.SvgRenderOptions.FromAppOptions(options!);
        renderOptions.Padding.ShouldBe(8d);
    }

    [Test]
    public void DefaultPaddingIsTwoWhenWindowIsNone()
    {
        var ok = ConsoleToSvg.Cli.OptionParser.TryParse(
            System.Array.Empty<string>(),
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var renderOptions = ConsoleToSvg.Svg.SvgRenderOptions.FromAppOptions(options!);
        renderOptions.Padding.ShouldBe(2d);
    }

    [Test]
    public void ExplicitPaddingOverridesWindowDefault()
    {
        var ok = ConsoleToSvg.Cli.OptionParser.TryParse(
            new[] { "--window", "macos", "--padding", "3" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var renderOptions = ConsoleToSvg.Svg.SvgRenderOptions.FromAppOptions(options!);
        renderOptions.Padding.ShouldBe(3d);
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
                Window = ConsoleToSvg.Svg.WindowStyle.WindowsPc,
                Padding = 2,
            }
        );

        // Windows Terminal style: control buttons as vector lines/rects, inside the window.
        // Active tab uses theme.Background (same as content area)
        // Tab close, +, |, v buttons all use stroke=#888888
        svg.ShouldContain("stroke=\"#888888\""); // tab icons (×, +, |, v)
        svg.ShouldContain("stroke=\"#cccccc\""); // window control buttons
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize □
        svg.ShouldContain("stroke-width=\"1.3\""); // close × lines

        // The desktop background uses a gradient
        svg.ShouldContain("linearGradient");
    }

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
                Window = ConsoleToSvg.Svg.WindowStyle.Windows,
                Padding = 2,
            }
        );

        // Windows Terminal style: tab shape and vector control buttons
        svg.ShouldContain("#1c1c1c"); // tab bar / outer fill
        svg.ShouldContain("polyline"); // tab icon chevron (>) and v-dropdown
        svg.ShouldContain("stroke=\"#888888\""); // tab icons (×, +, |, v)
        svg.ShouldContain("stroke=\"#cccccc\""); // window control icons
        svg.ShouldContain("fill=\"none\" stroke=\"#cccccc\""); // maximize □
        svg.ShouldContain("stroke-width=\"1.3\""); // close × lines
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
}
