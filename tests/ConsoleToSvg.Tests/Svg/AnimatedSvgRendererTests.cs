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
    public void RenderAnimatedSvgIncludesKeyframes()
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
        svg.ShouldContain("@keyframes c2k0");
        svg.ShouldContain("id=\"c2f0\"");
        svg.ShouldContain("animation:c2k0");
        svg.ShouldContain("linear forwards;");
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

        CountOccurrences(svg, "id=\"c2d").ShouldBe(1);
        CountOccurrences(svg, "<use href=\"#c2d0\"").ShouldBe(2);
        svg.ShouldContain("<rect class=\"u\" x=\"8.4\"");
        svg.ShouldContain("<rect class=\"u\" x=\"25.2\"");
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

        CountOccurrences(svg, "id=\"c2f").ShouldBe(2);
        svg.ShouldContain("<rect class=\"u\" x=\"16.8\"");
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

        CountOccurrences(svg, "id=\"c2f").ShouldBe(2);
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

        // The last frame's keyframe should end at opacity:1 (stays visible)
        // It should NOT contain a 100%{opacity:0} after the last frame's opacity:1 at 100%
        // Specifically: last @keyframes block should end with opacity:1 at 100%, not opacity:0
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes c2k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        // Last frame should not emit the fade-out rule (", 100% { opacity: 0; }")
        lastKeyframeBlock.ShouldNotContain("%, 100% {");
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

        // Single frame animation: the frame should stay visible
        svg.ShouldContain("@keyframes c2k0");
        var keyframeStart = svg.IndexOf("@keyframes c2k0", StringComparison.Ordinal);
        var keyframeBlock = svg.Substring(keyframeStart);
        keyframeBlock.ShouldNotContain("%, 100% {");
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

        var frameTagCount = CountOccurrences(svg, "id=\"c2f");
        frameTagCount.ShouldBeLessThan(20);
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

        svg.ShouldContain("linear infinite;");
        svg.ShouldNotContain("linear forwards;");
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

        var lowCount = CountOccurrences(lowFpsSvg, "id=\"c2f");
        var highCount = CountOccurrences(highFpsSvg, "id=\"c2f");
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

        // With fadeout, the last frame should end with opacity:0 at 100%
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes c2k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        lastKeyframeBlock.ShouldContain("%, 100% {");
        lastKeyframeBlock.ShouldContain("opacity: 0;");
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

        // Without fadeout, last frame should end at 100% opacity:1 (no fade)
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes c2k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        lastKeyframeBlock.ShouldNotContain("%, 100% {");
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

        GetFirstOpacityOnePercentage(GetKeyframeBlock(svg, 1)).ShouldBeLessThan(100d);
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

        var frame1Start = GetFirstOpacityOnePercentage(GetKeyframeBlock(svg, 1));
        var frame2Start = GetFirstOpacityOnePercentage(GetKeyframeBlock(svg, 2));
        var frame3Start = GetFirstOpacityOnePercentage(GetKeyframeBlock(svg, 3));

        frame1Start.ShouldBeLessThan(frame2Start);
        frame2Start.ShouldBeLessThan(frame3Start);
        frame3Start.ShouldBeLessThan(100d);
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

        // Unique frame content is stored in <defs>
        svg.ShouldContain("<defs>");
        svg.ShouldContain("id=\"c2d");

        // All animation frames reference content via <use>
        svg.ShouldContain("<use href=\"#c2d");
        svg.ShouldContain("id=\"c2f0\"");
        svg.ShouldContain("id=\"c2f1\"");
        svg.ShouldContain("id=\"c2f2\"");

        // Only 2 unique visual states → only 2 frame defs entries
        CountOccurrences(svg, "id=\"c2d").ShouldBe(2);
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
        svg.ShouldContain("id=\"c2d");
        svg.ShouldContain("href=\"#c2d");
        svg.ShouldContain("id=\"c2f0\" class=\"c2 f f0\"");
        svg.ShouldContain(".c2.f { opacity: 0; }");
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

        // Only 2 unique visual states regardless of how many times they repeat
        CountOccurrences(svg, "id=\"c2d").ShouldBe(2);

        // All animation frames use <use> elements (no <g class="frame"> outside defs)
        svg.ShouldContain("<use href=\"#c2d");
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

        CountOccurrences(svg, "id=\"c2d").ShouldBe(2);
        CountOccurrences(svg, "id=\"c2r").ShouldBe(3);
        CountOccurrences(svg, "<use href=\"#c2r").ShouldBe(5);
        svg.ShouldNotContain("transform=\"translate(0 0)\"");
        Regex.IsMatch(svg, """<use href="#c2r\d+" y="18"/>""").ShouldBeTrue();
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
        Regex.IsMatch(svg, """<g id="c2d\d+" class="c2" transform=""").ShouldBeFalse();
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

        CountOccurrences(svg, "<use href=\"#c2r").ShouldBeGreaterThan(8);
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
    public void RenderAnimatedSvgUsesFullFramesWhenRowsAreUnique()
    {
        var session = new RecordingSession(width: 4, height: 2);
        session.AddEvent(0.01, "AAAA\r\nBBBB");
        session.AddEvent(0.2, "\x1b[2J\x1b[HCCCC\r\nDDDD");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        CountOccurrences(svg, "id=\"c2d").ShouldBe(2);
        CountOccurrences(svg, "id=\"c2r").ShouldBe(0);
        CountOccurrences(svg, "<use href=\"#c2r").ShouldBe(0);
        svg.ShouldNotContain("id=\"c2e");
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
        var frameCount = CountOccurrences(svg, "id=\"c2f");
        frameCount.ShouldBeLessThan(20);
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

        svg.ShouldContain("id=\"c2f0\"");
        svg.ShouldNotContain("id=\"c2f1\"");
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

        svg.ShouldContain("id=\"c2f0\"");
        svg.ShouldNotContain("id=\"c2f1\"");
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

        svg.ShouldContain("id=\"c2f1\"");
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
    public void RenderAnimatedSvgUsesReadableSingleLineStyleBlock()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.2, "B");

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        svg.ShouldContain("<style>\n");
        svg.ShouldContain(".c2.f { opacity: 0; }");
        svg.ShouldContain("@keyframes c2k0 { 0%, ");
        svg.ShouldContain(".c2.f0 { animation:c2k0 ");

        // Non-keyframe rules must be grouped before the keyframes.
        var firstKeyframe = svg.IndexOf("@keyframes c2k0", StringComparison.Ordinal);
        var lastFrameRule = svg.LastIndexOf(".c2.f1 { animation:", StringComparison.Ordinal);
        firstKeyframe.ShouldBeGreaterThan(lastFrameRule);
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

    private static string GetKeyframeBlock(string svg, int frameIndex)
    {
        var token = $"@keyframes c2k{frameIndex} {{";
        var start = svg.IndexOf(token, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);

        var next = svg.IndexOf(
            $"@keyframes c2k{frameIndex + 1} {{",
            start + token.Length,
            StringComparison.Ordinal
        );
        if (next < 0)
        {
            next = svg.IndexOf("</style>", start, StringComparison.Ordinal);
        }

        next.ShouldBeGreaterThan(start);
        return svg.Substring(start, next - start);
    }

    private static double GetFirstOpacityOnePercentage(string keyframeBlock)
    {
        var match = Regex.Match(
            keyframeBlock,
            @"(?<percent>\d+(?:\.\d+)?)%(?:,\s*\d+(?:\.\d+)?%)?\s*\{\s*opacity:\s*1;"
        );
        match.Success.ShouldBeTrue();
        return double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);
    }

    private static double GetFirstAnimationDurationSeconds(string svg)
    {
        var match = Regex.Match(
            svg,
            @"animation:c2k\d+\s+(?<seconds>\d+(?:\.\d+)?)s\s+linear",
            RegexOptions.IgnoreCase
        );
        match.Success.ShouldBeTrue();
        return double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
    }
}
