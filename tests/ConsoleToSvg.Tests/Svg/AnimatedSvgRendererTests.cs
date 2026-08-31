using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Svg;

public sealed class AnimatedSvgRendererTests
{
    [Test]
    public void WriteMatchesStringRender()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "A");
        session.AddEvent(0.2, "B");
        var options = new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" };
        using var writer = new StringWriter();

        ConsoleToSvg.Svg.AnimatedSvgRenderer.Write(writer, session, options);

        writer.ToString().ShouldBe(
            ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(session, options)
        );
    }

    [Test]
    public void RenderFramesStartsWithProvidedTerminalState()
    {
        var emulator = new TerminalEmulator(12, 2, Theme.Resolve("dark"));
        emulator.Process("before");
        var initial = emulator.Buffer.Clone();
        emulator.Process("\rafter");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.RenderFrames(
            [
                new TerminalFrame(0, initial),
                new TerminalFrame(0.2, emulator.Buffer.Clone()),
            ],
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoFps = 30 }
        );

        svg.ShouldContain("before");
        svg.ShouldContain("after");
    }

    [Test]
    public void RenderAnimatedSvgIncludesDiscreteSmilDisplayAnimation()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("0", "0", "0", "0"),
            }
        );

        svg.ShouldContain("<svg");
        svg.ShouldContain("<animate attributeName=\"display\"");
        svg.ShouldContain("calcMode=\"discrete\"");
        svg.ShouldContain("fill=\"freeze\"");
        svg.ShouldNotContain("@keyframes c2k");
    }

    [Test]
    public void RenderAnimatedSvgTracksCursorAcrossFrames()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<rect class=\"u\" x=\"8.4\"");
        svg.ShouldContain("<rect class=\"u\" x=\"16.8\"");
    }

    [Test]
    public void RenderAnimatedSvgSharesContentDefinitionsAcrossCursorPositions()
    {
        var emulator = new TerminalEmulator(8, 2, Theme.Resolve("dark"));
        emulator.Process("A");
        var first = emulator.Buffer.Clone();
        emulator.Process("\u001b[2C");
        var second = emulator.Buffer.Clone();

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.RenderFrames(
            [new TerminalFrame(0.01, first), new TerminalFrame(0.2, second)],
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoFps = 0 }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBe(2);
        CountOccurrences(svg, "<use href=\"#c2r").ShouldBe(2);
        svg.ShouldContain("<rect class=\"u\" x=\"8.4\"");
        svg.ShouldContain("<rect class=\"u\" x=\"25.2\"");

        var document = System.Xml.Linq.XDocument.Parse(svg);
        var cursorRects = document
            .Descendants()
            .Where(element => (string?)element.Attribute("class") == "u")
            .ToArray();
        cursorRects.Length.ShouldBe(2);
        cursorRects
            .All(rect =>
                rect.Parent
                    ?.Elements()
                    .Any(element =>
                        element.Name.LocalName == "animate"
                        && (string?)element.Attribute("attributeName") == "display"
                    ) == true
            )
            .ShouldBeTrue();
    }

    [Test]
    public void RenderAnimatedSvgOmitsCursorOnlyFrames()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(1.0, "\u001b[2C");
        session.AddEvent(2.0, "\u001b[2D");
        session.AddEvent(3.0, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBe(3);
        svg.ShouldContain("<rect class=\"u\" x=\"16.8\"");
        svg.ShouldNotContain("<rect class=\"u\" x=\"33.6\"");
    }

    [Test]
    public void RenderAnimatedSvgOmitsUnchangedFramesBeyondSamplingInterval()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(1.0, "\u001b[31m");
        session.AddEvent(2.0, "\u001b[1m");
        session.AddEvent(3.0, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBe(3);
    }

    [Test]
    public void RenderAnimatedSvgLastFrameDoesNotFadeToBlack()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldNotContain("<animate attributeName=\"opacity\"");
    }

    [Test]
    public void RenderAnimatedSvgSingleFrameDoesNotFadeToBlack()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "A");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("display=\"inline\"");
        svg.ShouldNotContain("<animate attributeName=\"opacity\"");
    }

    [Test]
    public void RenderAnimatedSvgDownsamplesDenseFrames()
    {
        var session = new RecordingSession(width: 8, height: 2);
        for (var i = 0; i < 60; i++)
        {
            session.AddEvent(i * 0.01, "A");
        }

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBeLessThan(20);
    }

    [Test]
    public void RenderAnimatedSvgWithLoopUsesInfiniteAnimation()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Loop = true }
        );

        svg.ShouldContain("repeatCount=\"indefinite\"");
        svg.ShouldNotContain("fill=\"freeze\"");
    }

    [Test]
    public void RenderAnimatedSvgPreservesCmatrixHighlightColors()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.00, "\u001b[32mA");
        session.AddEvent(0.01, "\u001b[37mA");
        session.AddEvent(0.02, "\u001b[32mA");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("#0dbc79");
        svg.ShouldContain("#e5e5e5");
    }

    [Test]
    public void RenderAnimatedSvgHigherFpsKeepsMoreFrames()
    {
        var session = new RecordingSession(width: 8, height: 2);
        for (var i = 0; i < 60; i++)
        {
            session.AddEvent(i * 0.01, "A");
        }

        var lowFpsSvg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoFps = 8 }
        );
        var highFpsSvg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoFps = 30 }
        );

        var lowCount = CountOccurrences(lowFpsSvg, "id=\"c2r");
        var highCount = CountOccurrences(highFpsSvg, "id=\"c2r");
        highCount.ShouldBeGreaterThan(lowCount);
    }

    [Test]
    public void RenderAnimatedSvgWithSleepExtendsAnimationDuration()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(1.0, "B");

        var noSleepSvg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoSleep = 0 }
        );
        var withSleepSvg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoSleep = 2 }
        );

        // With sleep=2, the animation duration should be ~3s vs ~1s without sleep
        var noSleepDuration = GetFirstAnimationDurationSeconds(noSleepSvg);
        var withSleepDuration = GetFirstAnimationDurationSeconds(withSleepSvg);
        withSleepDuration.ShouldBeGreaterThan(noSleepDuration + 1.5d);
        withSleepDuration.ShouldBeLessThan(noSleepDuration + 2.5d);
    }

    [Test]
    public void RenderAnimatedSvgWithFadeOutLastFrameFadesOut()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(1.0, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoSleep = 0,
                VideoFadeOut = 0.5,
            }
        );

        svg.ShouldContain(
            "<animate attributeName=\"opacity\" values=\"1;1;0\""
        );
        svg.ShouldContain(";1\" dur=\"");
    }

    [Test]
    public void RenderAnimatedSvgNoFadeOutLastFrameStaysVisible()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(1.0, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoSleep = 1,
                VideoFadeOut = 0,
            }
        );

        svg.ShouldNotContain("<animate attributeName=\"opacity\"");
    }

    [Test]
    public void RenderAnimatedSvgWithZeroSleepStartsLastFrameBeforeAnimationEnd()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.10, "A");
        session.AddEvent(0.20, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Loop = true,
                VideoSleep = 0,
                VideoFadeOut = 0,
            }
        );

        GetDisplayStartKeyTimes(svg).Last().ShouldBeLessThan(1d);
    }

    [Test]
    public void RenderAnimatedSvgSpreadsCollapsedTailFrameTimes()
    {
        var session = new RecordingSession(width: 16, height: 2);
        session.AddEvent(0.10, "\u001b[2J\u001b[HA");
        session.AddEvent(0.20, "\u001b[2J\u001b[HB");
        session.AddEvent(0.20, "\u001b[2J\u001b[HC");
        session.AddEvent(0.20, "\u001b[2J\u001b[HD");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Loop = true,
                VideoSleep = 0,
                VideoFadeOut = 0,
            }
        );

        var starts = GetDisplayStartKeyTimes(svg);
        starts.Length.ShouldBeGreaterThanOrEqualTo(3);
        starts[0].ShouldBeLessThan(starts[1]);
        starts[1].ShouldBeLessThan(starts[2]);
        starts[2].ShouldBeLessThan(1d);
    }

    [Test]
    public void RenderAnimatedSvgDeduplicatesIdenticalFrames()
    {
        // A looping animation: the terminal returns to the same visual state, so the
        // repeated state should share one <defs> entry instead of duplicating SVG content.
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A"); // state: "A"
        session.AddEvent(0.5, "\r\x1b[2J\x1b[H"); // state: blank screen
        session.AddEvent(1.0, "A"); // state: "A" again — identical to first

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // Unique row content is stored in <defs>.
        svg.ShouldContain("<defs>");
        svg.ShouldContain("id=\"c2r");

        // Repeated row states are represented as separate display runs that share definitions.
        svg.ShouldContain("<use href=\"#c2r");
        svg.ShouldContain("values=\"inline;none\"");
        svg.ShouldContain("values=\"none;inline;none\"");

        CountOccurrences(svg, "id=\"c2r").ShouldBe(2);
    }

    [Test]
    public void RenderAnimatedSvgNamespacesGeneratedSelectorsAndIdsFromCustomChrome()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");
        var chrome = new ConsoleToSvg.Svg.ChromeDefinition
        {
            SvgTemplate = "<g id=\"d0\" class=\"f i q a\"><text>chrome</text></g>",
        };

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Chrome = chrome }
        );

        svg.ShouldContain("<g id=\"d0\" class=\"f i q a\">");
        svg.ShouldContain("id=\"c2r");
        svg.ShouldContain("href=\"#c2r");
        svg.ShouldContain("attributeName=\"display\"");
        svg.ShouldNotContain(".c2.f { opacity: 0; }");
        svg.ShouldContain(".c2 .c2b { animation: c2b");
        svg.ShouldContain(".c2 .q { shape-rendering:");
        svg.ShouldContain("\n.c2 .aa{fill:");
        CountOccurrences(svg, "<style>").ShouldBe(1);
        svg.ShouldNotContain("\n.f {");
        svg.ShouldNotContain("\n.i {");
        svg.ShouldNotContain("\n.q {");
        svg.ShouldNotContain("<style>.a{");

        var document = System.Xml.Linq.XDocument.Parse(svg);
        var chromeNode = document.Descendants().Single(element =>
            string.Equals((string?)element.Attribute("id"), "d0", StringComparison.Ordinal)
        );
        chromeNode
            .Ancestors()
            .Any(element =>
                ((string?)element.Attribute("class"))
                    ?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("c2", StringComparer.Ordinal) == true
            )
            .ShouldBeFalse();
    }

    [Test]
    public void RenderAnimatedSvgDeduplicatedSvgSmallerWhenFramesRepeat()
    {
        // Build a session where content cycles between two states many times.
        // With deduplication, all repeated frames share defs → smaller output.
        var session = new RecordingSession(width: 40, height: 10);
        for (var cycle = 0; cycle < 10; cycle++)
        {
            // Clear screen + cursor home before each state so each cycle produces the same visual output
            session.AddEvent(cycle * 0.2, "\x1b[2J\x1b[H\x1b[32mHello World\x1b[m");
            session.AddEvent(cycle * 0.2 + 0.1, "\x1b[2J\x1b[H\x1b[31mGoodbye\x1b[m");
        }

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // Only the two changing rows and the shared blank row are defined.
        CountOccurrences(svg, "id=\"c2r").ShouldBe(3);

        svg.ShouldContain("<use href=\"#c2r");
    }

    [Test]
    public void RenderAnimatedSvgSharesUnchangedRowsBetweenFrames()
    {
        var session = new RecordingSession(width: 4, height: 2);
        session.AddEvent(0.01, "AAAA");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBe(3);
        // Three animated row runs plus one delta-definition reference.
        CountOccurrences(svg, "<use href=\"#c2r").ShouldBe(4);
        svg.ShouldNotContain("transform=\"translate(0 0)\"");
        Regex.IsMatch(svg, """<use href="#c2r\d+" y="18" display=""").ShouldBeTrue();
        Regex.IsMatch(svg, """<use href="#c2r\d+" transform=""").ShouldBeFalse();
    }

    [Test]
    public void RenderAnimatedSvgHoistsSharedContentTranslation()
    {
        var session = new RecordingSession(width: 4, height: 2);
        session.AddEvent(0.01, "AAAA");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", Padding = 8 }
        );

        CountOccurrences(svg, "transform=\"translate(8 8)\"").ShouldBe(1);
        Regex.IsMatch(svg, """<g id="c2r\d+" class="c2 c" transform=""").ShouldBeFalse();
    }

    [Test]
    public void RenderAnimatedSvgReusesGraphicsButKeepsTextInline()
    {
        var emulator = new TerminalEmulator(8, 4, Theme.Resolve("dark"));
        emulator.Process("│A\r\n│B\r\nAA█\r\nAA▀");
        var first = emulator.Buffer.Clone();
        emulator.Process("\u001b[1;2HC");
        var second = emulator.Buffer.Clone();

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.RenderFrames(
            [new TerminalFrame(0d, first), new TerminalFrame(0.2d, second)],
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoFps = 0,
            }
        );

        var document = System.Xml.Linq.XDocument.Parse(svg);
        var ns = document.Root!.Name.Namespace;
        var reusedIds = document
            .Descendants(ns + "use")
            .Select(node => ((string?)node.Attribute("href"))?.TrimStart('#'))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var elementName in new[] { "path", "rect" })
        {
            document
                .Descendants(ns + elementName)
                .Any(node => reusedIds.Contains((string?)node.Attribute("id")))
                .ShouldBeTrue();
        }
        document
            .Descendants(ns + "text")
            .Any(node => ((string?)node.Attribute("id"))?.StartsWith("c2e") == true)
            .ShouldBeFalse();
    }

    [Test]
    public void RenderAnimatedSvgPreservesTextOptionsWithCompactStyles()
    {
        var session = new RecordingSession(width: 12, height: 2);
        session.AddEvent(0.01, "\u001b[5msecret");
        session.AddEvent(0.2, "!");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                CommandHeader = "secret header",
                LengthAdjust = "spacingAndGlyphs",
                MaskPatterns = ["secret"],
            }
        );

        svg.ShouldNotContain("secret");
        svg.ShouldContain("******");
        svg.ShouldContain("lengthAdjust=\"spacingAndGlyphs\"");
        svg.ShouldContain(" c2b\"");
        svg.ShouldContain("@keyframes c2b");
        svg.ShouldContain("<g class=\"c2 c\"><rect");
        svg.ShouldNotContain("<text class=\"c");
        System.Xml.Linq.XDocument.Parse(svg).Root.ShouldNotBeNull();
    }

    [Test]
    public void RenderAnimatedSvgCachesLocalizedColumnChanges()
    {
        var session = new RecordingSession(width: 40, height: 2);
        session.AddEvent(0.01, "prompt$ ");
        session.AddEvent(0.2, "a");
        session.AddEvent(0.4, "p");
        session.AddEvent(0.6, "t");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "<use href=\"#c2r").ShouldBeGreaterThanOrEqualTo(8);
        svg.ShouldContain("<g id=\"c2r");
        svg.ShouldContain("class=\"c2 c\"><use href=\"#c2r");
    }

    [Test]
    public void RenderAnimatedSvgDeltaBackgroundCoversPreviousBackgroundStroke()
    {
        var session = new RecordingSession(width: 80, height: 2);
        session.AddEvent(0.01, "\u001b[60G\u001b[48;2;135;215;135m Saving... \u001b[0m");
        session.AddEvent(
            0.2,
            "\u001b[60G\u001b[13X\u001b[64G\u001b[48;2;135;215;135m Saved \u001b[0m"
        );

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("class=\"c2 c\"><use href=\"#c2r");
        svg.ShouldContain(
            "fill=\"#1e1e1e\" stroke=\"#1e1e1e\" stroke-width=\"2\""
                + " vector-effect=\"non-scaling-stroke\""
        );
    }

    [Test]
    public void RenderAnimatedSvgLimitsColumnCacheDepth()
    {
        var session = new RecordingSession(width: 40, height: 2);
        for (var i = 0; i < 7; i++)
        {
            session.AddEvent(i * 0.2, ((char)('a' + i)).ToString());
        }

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "class=\"c2 c\"><use href=\"#c2r").ShouldBe(5);
    }

    [Test]
    public void RenderAnimatedSvgUsesRowDefinitionsWhenRowsAreUnique()
    {
        var session = new RecordingSession(width: 4, height: 2);
        session.AddEvent(0.01, "AAAA\r\nBBBB");
        session.AddEvent(0.2, "\x1b[2J\x1b[HCCCC\r\nDDDD");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2r").ShouldBe(4);
        CountOccurrences(svg, "<use href=\"#c2r").ShouldBe(4);
        CountOccurrences(svg, "attributeName=\"display\"").ShouldBeGreaterThanOrEqualTo(4);
    }

    [Test]
    public void RenderAnimatedSvgColorOnlyChangesAreRateLimited()
    {
        // Simulate cmatrix-like output: 30 rapid color-changing frames over 1 second (30fps input).
        // With a 12fps target, the output should keep at most ~12 frames, NOT all 30.
        // Before the ReduceFrames fix, color-changing frames bypassed the FPS limit entirely.
        var session = new RecordingSession(width: 8, height: 2);
        for (var i = 0; i < 30; i++)
        {
            // Each event changes color and content at 30fps (every ~33ms)
            var color = (i % 2 == 0) ? "\x1b[32m" : "\x1b[33m";
            session.AddEvent(i * (1.0 / 30), $"\x1b[2J\x1b[H{color}{i}");
        }

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark", VideoFps = 12 }
        );

        // At 12fps over ~1 second, at most 12 + 2 (first + last) frames should be kept.
        // The old code kept ALL 30 frames because every frame had a color change.
        // The retained changing rows plus the shared blank row remain bounded.
        CountOccurrences(svg, "id=\"c2r").ShouldBeLessThanOrEqualTo(21);
    }

    [Test]
    public void RenderAnimatedSvgDropsTrailingBlankFrameFromAlternateScreenLeave()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "\u001b[?1049hHELLO");
        session.AddEvent(0.2, "\u001b[?1049l");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("HELLO");
        svg.ShouldNotContain("attributeName=\"display\"");
    }

    [Test]
    public void RenderAnimatedSvgDropsTrailingBlankFrameFromClearHomeTail()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.1, "HELLO");
        session.AddEvent(0.2, "\u001b[2J\u001b[H");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("HELLO");
        svg.ShouldNotContain("attributeName=\"display\"");
    }

    [Test]
    public void RenderAnimatedSvgKeepsUnrelatedFinalBlankAfterDiscardedClearAndRedraw()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.00, "HELLO");
        session.AddEvent(0.01, "\u001b[2J\u001b[HHELLO");
        session.AddEvent(0.20, "\b\b\b\b\b     ");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoFps = 12,
                VideoTiming = ConsoleToSvg.Svg.VideoTimingMode.Realtime,
            }
        );

        svg.ShouldContain("attributeName=\"display\"");
    }

    [Test]
    public void RenderAnimatedSvgKeepsPendingVisualChangeBeforeLaterBoundary()
    {
        var session = new RecordingSession(width: 20, height: 2);
        session.AddEvent(0.00, "\u001b[2J\u001b[HBASE-A");
        session.AddEvent(1.00, "\u001b[2J\u001b[HBASE-B");
        session.AddEvent(1.02, "\u001b[2J\u001b[HMID-C");
        session.AddEvent(1.04, "\u001b[2J\u001b[HMID-D");
        session.AddEvent(2.00, "\u001b[2J\u001b[HSHELL-E");
        session.AddEvent(3.00, "\u001b[2J\u001b[HEXIT-F");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoFps = 1,
                VideoTiming = ConsoleToSvg.Svg.VideoTimingMode.Realtime,
            }
        );

        svg.ShouldContain(">MID-D<");
        svg.ShouldContain(">SHELL-E<");
        svg.ShouldContain(">EXIT-F<");
    }

    [Test]
    public void RenderAnimatedSvgPreservesFirstStateBeforeQuickFinalExit()
    {
        var session = new RecordingSession(width: 30, height: 3);
        session.AddEvent(0.00, "\u001b[2J\u001b[HVIM-Q");
        session.AddEvent(1.00, "\u001b[2J\u001b[HAFTER-Q");
        session.AddEvent(1.01, "\u001b[2J\u001b[HSHELL-PROMPT");
        session.AddEvent(1.02, "\u001b[2J\u001b[HLOGOUT-TEXT");
        session.AddEvent(2.00, "\u001b[2J\u001b[HEXIT-END");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                VideoFps = 1,
                VideoTiming = ConsoleToSvg.Svg.VideoTimingMode.Realtime,
            }
        );

        svg.ShouldContain(">LOGOUT-TEXT<");
        svg.ShouldContain(">EXIT-END<");
    }

    [Test]
    public void RenderAnimatedSvgDeterministicTimingSuppressesSmallJitterDiffs()
    {
        var sessionA = new RecordingSession(width: 8, height: 2);
        sessionA.AddEvent(0.100, "A");
        sessionA.AddEvent(0.201, "B");
        sessionA.AddEvent(0.302, "C");

        var sessionB = new RecordingSession(width: 8, height: 2);
        sessionB.AddEvent(0.099, "A");
        sessionB.AddEvent(0.199, "B");
        sessionB.AddEvent(0.304, "C");

        var options = new ConsoleToSvg.Svg.SvgRenderOptions
        {
            Theme = "dark",
            VideoFps = 12,
            VideoTiming = ConsoleToSvg.Svg.VideoTimingMode.Deterministic,
        };

        var svgA = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(sessionA, options);
        var svgB = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(sessionB, options);

        svgA.ShouldBe(svgB);
    }

    [Test]
    public void RenderAnimatedSvgKeepsSmilAnimationOutsideStyleBlock()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<style>\n");
        svg.ShouldNotContain(".c2.f");
        var styleEnd = svg.IndexOf("</style>", StringComparison.Ordinal);
        var animation = svg.IndexOf(
            "<animate attributeName=\"display\"",
            StringComparison.Ordinal
        );
        animation.ShouldBeGreaterThan(styleEnd);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(token, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += token.Length;
        }
    }

    private static double[] GetDisplayStartKeyTimes(string svg) =>
        Regex
            .Matches(
                svg,
                """values="none;inline(?:;none)?" keyTimes="0;(?<start>\d+(?:\.\d+)?)"""
            )
            .Select(match =>
                double.Parse(
                    match.Groups["start"].Value,
                    CultureInfo.InvariantCulture
                )
            )
            .Distinct()
            .Order()
            .ToArray();

    private static double GetFirstAnimationDurationSeconds(string svg)
    {
        var match = Regex.Match(
            svg,
            """attributeName="display"[^>]* dur="(?<seconds>\d+(?:\.\d+)?)s""",
            RegexOptions.IgnoreCase
        );
        match.Success.ShouldBeTrue();
        return double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
    }
}
