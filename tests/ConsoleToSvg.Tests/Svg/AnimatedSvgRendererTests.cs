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
