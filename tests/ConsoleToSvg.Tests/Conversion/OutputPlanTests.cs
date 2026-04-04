using System;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Conversion;

namespace ConsoleToSvg.Tests.Conversion;

public sealed class OutputPlanTests
{
    [Test]
    public void PngOutputRequiresStaticRasterPlan()
    {
        var plan = OutputPlan.Create(
            new AppOptions
            {
                OutputPath = "output.png",
                Mode = OutputMode.Image,
            }
        );

        plan.Format.ShouldBe(OutputFormat.Png);
        plan.RequiresResvg.ShouldBeTrue();
        plan.RequiresFfmpeg.ShouldBeFalse();
        plan.RequiresStaticSvg.ShouldBeTrue();
    }

    [Test]
    public void Mp4OutputRequiresAnimatedFramePlan()
    {
        var plan = OutputPlan.Create(
            new AppOptions
            {
                OutputPath = "output.mp4",
                Mode = OutputMode.Video,
            }
        );

        plan.Format.ShouldBe(OutputFormat.Mp4);
        plan.RequiresAnimatedFrames.ShouldBeTrue();
        plan.RequiresFfmpeg.ShouldBeTrue();
    }

    [Test]
    public void StdOutRejectsRasterFormats()
    {
        Should.Throw<InvalidOperationException>(
            () =>
                OutputPlan.Create(
                    new AppOptions
                    {
                        OutputPath = "output.png",
                        StdOut = true,
                    }
                )
        ).Message.ShouldBe("--stdout only supports SVG output.");
    }

    [Test]
    public void AnimatedFormatsRequireVideoOrRepeatMode()
    {
        Should.Throw<InvalidOperationException>(
            () =>
                OutputPlan.Create(
                    new AppOptions
                    {
                        OutputPath = "output.gif",
                        Mode = OutputMode.Image,
                    }
                )
        ).Message.ShouldContain("requires --mode video");
    }

    [Test]
    public void StaticRasterFormatsRequireImageMode()
    {
        Should.Throw<InvalidOperationException>(
            () =>
                OutputPlan.Create(
                    new AppOptions
                    {
                        OutputPath = "output.webp",
                        Mode = OutputMode.Video,
                    }
                )
        ).Message.ShouldContain("requires image mode");
    }
}
