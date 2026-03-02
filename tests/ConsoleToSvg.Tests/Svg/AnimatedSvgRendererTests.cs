using System;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed class AnimatedSvgRendererTests
{
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
        svg.ShouldContain("@keyframes k0");
        svg.ShouldContain("frame-0");
        svg.ShouldContain("animation:k0");
        svg.ShouldContain("linear forwards;");
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
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        // Last frame should have opacity:1 at end (100%), not followed by opacity:0
        lastKeyframeBlock.ShouldContain("100%{opacity:1;}");
        lastKeyframeBlock.ShouldNotContain("100%{opacity:0;}");
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
        svg.ShouldContain("@keyframes k0");
        var keyframeStart = svg.IndexOf("@keyframes k0", StringComparison.Ordinal);
        var keyframeBlock = svg.Substring(keyframeStart);
        keyframeBlock.ShouldNotContain("100%{opacity:0;}");
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

        var frameTagCount = CountOccurrences(svg, "id=\"frame-");
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
    public void RenderAnimatedSvgPreservesRapidColorChanges()
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
        svg.ShouldContain("#23D18B");
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

        var lowCount = CountOccurrences(lowFpsSvg, "id=\"frame-");
        var highCount = CountOccurrences(highFpsSvg, "id=\"frame-");
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
        withSleepSvg.ShouldContain("3s linear");
        noSleepSvg.ShouldNotContain("3s linear");
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
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        lastKeyframeBlock.ShouldContain("100%{opacity:0;}");
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
        var lastKeyframeIndex = svg.LastIndexOf("@keyframes k", StringComparison.Ordinal);
        lastKeyframeIndex.ShouldBeGreaterThanOrEqualTo(0);
        var lastKeyframeBlock = svg.Substring(lastKeyframeIndex);
        lastKeyframeBlock.ShouldContain("100%{opacity:1;}");
        lastKeyframeBlock.ShouldNotContain("100%{opacity:0;}");
    }

    [Test]
    public void RenderAnimatedSvgDeduplicatesIdenticalFrames()
    {
        // A looping animation: the terminal returns to the same visual state, so the
        // repeated state should share one <defs> entry instead of duplicating SVG content.
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");                    // state: "A"
        session.AddEvent(0.5, "\r\x1b[2J\x1b[H");      // state: blank screen
        session.AddEvent(1.0, "A");                     // state: "A" again — identical to first

        var svg = ConsoleToSvg.Svg.AnimatedSvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions { Theme = "dark" }
        );

        // Unique frame content is stored in <defs>
        svg.ShouldContain("<defs>");
        svg.ShouldContain("id=\"fd-");

        // All animation frames reference content via <use>
        svg.ShouldContain("<use href=\"#fd-");
        svg.ShouldContain("id=\"frame-0\"");
        svg.ShouldContain("id=\"frame-1\"");
        svg.ShouldContain("id=\"frame-2\"");

        // Only 2 unique visual states → only 2 fd- defs entries
        CountOccurrences(svg, "id=\"fd-").ShouldBe(2);
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
        CountOccurrences(svg, "id=\"fd-").ShouldBe(2);

        // All animation frames use <use> elements (no <g class="frame"> outside defs)
        svg.ShouldContain("<use href=\"#fd-");
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
        var frameCount = CountOccurrences(svg, "id=\"frame-");
        frameCount.ShouldBeLessThan(20);
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
}
