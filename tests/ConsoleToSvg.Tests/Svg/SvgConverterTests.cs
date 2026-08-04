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

        var args = (string[])method.Invoke(null, [2.5d, "output.mp4", "mpeg4"])!;

        // Verify structure: codec should be at index 10 (after "-c:v") for MP4
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
        args[10].ShouldBe("mpeg4");
        args[11].ShouldBe("-pix_fmt");
        args[12].ShouldBe("yuv420p");
        args[13].ShouldBe("output.mp4");
    }

    [Test]
    public void InMemoryVideoOmitsCodecForNonMp4Formats()
    {
        var method = typeof(SvgConverter).GetMethod(
            "CreateInMemoryVideoFfmpegArgs",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.ShouldNotBeNull();

        // Test GIF output
        var gifArgs = (string[])method.Invoke(null, [2.5d, "output.gif", null])!;
        gifArgs.Length.ShouldBe(12);
        gifArgs[0].ShouldBe("-y");
        gifArgs[1].ShouldBe("-framerate");
        gifArgs[2].ShouldBe("2.5");
        gifArgs[3].ShouldBe("-f");
        gifArgs[4].ShouldBe("image2pipe");
        gifArgs[5].ShouldBe("-vcodec");
        gifArgs[6].ShouldBe("png");
        gifArgs[7].ShouldBe("-i");
        gifArgs[8].ShouldBe("pipe:0");
        gifArgs[9].ShouldBe("-pix_fmt");
        gifArgs[10].ShouldBe("yuv420p");
        gifArgs[11].ShouldBe("output.gif");

        // Test WebM output
        var webmArgs = (string[])method.Invoke(null, [2.5d, "output.webm", null])!;
        webmArgs.Length.ShouldBe(12);
        webmArgs[11].ShouldBe("output.webm");
    }
}
