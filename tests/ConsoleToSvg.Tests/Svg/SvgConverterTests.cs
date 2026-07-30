using System;
using System.Diagnostics;
using System.Reflection;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Svg;

public sealed class SvgConverterTests
{
    [Test]
    public void FfmpegProcessRedirectsOutputAwayFromParentTerminal()
    {
        var method = typeof(SvgConverter).GetMethod(
            "CreateFfmpegStartInfo",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.ShouldNotBeNull();

        var startInfo = (ProcessStartInfo)method.Invoke(
            null,
            ["ffmpeg", new[] { "-version" }]
        )!;

        startInfo.UseShellExecute.ShouldBeFalse();
        startInfo.RedirectStandardOutput.ShouldBeTrue();
        startInfo.RedirectStandardError.ShouldBeTrue();
    }

    [Test]
    public void InMemoryVideoUsesImagePipeAndInvariantFramerate()
    {
        var method = typeof(SvgConverter).GetMethod(
            "CreateInMemoryVideoFfmpegArgs",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.ShouldNotBeNull();

        var args = (string[])method.Invoke(null, [2.5d, "png", "output.mp4"])!;

        args.ShouldBe(
        [
            "-y", "-framerate", "2.5", "-f", "image2pipe", "-vcodec", "png",
            "-i", "pipe:0", "output.mp4",
        ]
        );
    }
}
