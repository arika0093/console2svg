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
}
