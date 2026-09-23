using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Svg;

public sealed class SvgConverterTests
{
    [Test]
    public void ResolveFfmpegExecutableResolvesBareCommandFromPath()
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "sh";
        var method = typeof(SvgConverter).GetMethod(
            "ResolveFfmpegExecutable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.ShouldNotBeNull();

        var resolved = (string)method.Invoke(null, [command])!;

        resolved.ShouldNotBeEmpty();
        Path.IsPathFullyQualified(resolved).ShouldBeTrue();
        File.Exists(resolved).ShouldBeTrue();
    }

    [Test]
    public void FfmpegProcessRedirectsOutputAwayFromParentTerminal()
    {
        var method = typeof(SvgConverter).GetMethod(
            "CreateFfmpegStartInfo",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.ShouldNotBeNull();

        var startInfo = (ProcessStartInfo)method.Invoke(null, ["ffmpeg", new[] { "-version" }])!;

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
        args.Length.ShouldBe(16);
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
        args[13].ShouldBe("-vf");
        args[14].ShouldBe("pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0");
        args[15].ShouldBe("output.mp4");
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
        gifArgs.Length.ShouldBe(14);
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
        gifArgs[11].ShouldBe("-vf");
        gifArgs[12].ShouldBe("pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0");
        gifArgs[13].ShouldBe("output.gif");

        // Test WebM output
        var webmArgs = (string[])method.Invoke(null, [2.5d, "output.webm", null])!;
        webmArgs.Length.ShouldBe(14);
        webmArgs[13].ShouldBe("output.webm");
    }

    [Test]
    public void BundledResvgVersionMatchesCargoLockOrUnknown()
    {
        if (!SvgConverter.IsResvgAvailable)
        {
            SvgConverter.BundledResvgVersion.ShouldBe("unknown");
            return;
        }

        var cargoLock = FindResvgWrapperCargoLock();
        cargoLock.ShouldNotBeNull();

        var expected = FindLockedPackageVersion(cargoLock, "resvg");
        expected.ShouldNotBeNull();

        SvgConverter.BundledResvgVersion.ShouldBe(expected);
    }

    private static string FindResvgWrapperCargoLock()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ConsoleToSvg.Converter",
                "native",
                "resvg-wrapper",
                "Cargo.lock"
            );
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate resvg-wrapper Cargo.lock.");
    }

    private static string FindLockedPackageVersion(string cargoLock, string name)
    {
        var inPackage = false;
        var isTarget = false;
        string? version = null;
        foreach (var rawLine in File.ReadAllLines(cargoLock))
        {
            var line = rawLine.Trim();
            if (line == "[[package]]")
            {
                if (isTarget && version is not null)
                {
                    return version;
                }
                inPackage = true;
                isTarget = false;
                version = null;
                continue;
            }
            if (!inPackage)
            {
                continue;
            }
            if (line.StartsWith("name = ", StringComparison.Ordinal))
            {
                var packageName = line.Replace("name = ", string.Empty).Replace("\"", string.Empty);
                isTarget = packageName == name;
            }
            else if (
                line.StartsWith("version = ", StringComparison.Ordinal) && version is null
            )
            {
                version = line.Replace("version = ", string.Empty).Replace("\"", string.Empty);
            }
            if (
                isTarget
                && version is not null
                && line.StartsWith("source = ", StringComparison.Ordinal)
            )
            {
                return version;
            }
        }
        if (isTarget && version is not null)
        {
            return version;
        }
        throw new InvalidOperationException("Could not find package in Cargo.lock.");
    }
}
