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

        var args = (string[])method.Invoke(null, [2.5d, "output.mp4"])!;

        // Verify structure: codec should be at index 10 (after "-c:v")
        args.Length.ShouldBe(14);
        args[0].ShouldBe("-y");
        args[1].ShouldBe("-framerate");
        args[2].ShouldBe("2.5");
        args[3].ShouldBe("-f");
        args[4].ShouldBe("image2pipe");
        args[5].ShouldBe("-vcodec");
        args[6].ShouldBe("png");
        args[7].ShouldBe("-i");
        args[8].ShouldBe("pipe:0");
        args[9].ShouldBe("-c:v");
        // Index 10: codec (libx264 or mpeg4 depending on availability)
        (args[10] == "libx264" || args[10] == "mpeg4").ShouldBeTrue();
        args[11].ShouldBe("-pix_fmt");
        args[12].ShouldBe("yuv420p");
        args[13].ShouldBe("output.mp4");
    }
}
