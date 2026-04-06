using System;
using System.IO;
using ConsoleToSvg.Conversion;

namespace ConsoleToSvg.Tests.Conversion;

public sealed class OutputConverterTests
{
    [Test]
    public void GetExecutableFileName_AppendsExeOnWindows()
    {
        OutputConverter.GetExecutableFileName("resvg", isWindows: true).ShouldBe("resvg.exe");
        OutputConverter.GetExecutableFileName("resvg", isWindows: false).ShouldBe("resvg");
    }

    [Test]
    public void TryResolveExecutable_PrefersBundledBinaryOverPath()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var appDir = Path.Combine(tempRoot, "app");
            var pathDir = Path.Combine(tempRoot, "path");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(pathDir);

            var bundled = Path.Combine(appDir, "resvg");
            var onPath = Path.Combine(pathDir, "resvg");
            File.WriteAllText(bundled, string.Empty);
            File.WriteAllText(onPath, string.Empty);

            var resolved = OutputConverter.TryResolveExecutable(
                "resvg",
                isWindows: false,
                processPath: Path.Combine(appDir, "console2svg"),
                currentDirectory: null,
                pathEnvironment: pathDir
            );

            resolved.ShouldBe(bundled);
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    [Test]
    public void TryResolveExecutable_ChecksCurrentDirectoryBeforePath()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var currentDir = Path.Combine(tempRoot, "cwd");
            var pathDir = Path.Combine(tempRoot, "path");
            Directory.CreateDirectory(currentDir);
            Directory.CreateDirectory(pathDir);

            var fromCurrentDirectory = Path.Combine(currentDir, "ffmpeg.exe");
            var onPath = Path.Combine(pathDir, "ffmpeg.exe");
            File.WriteAllText(fromCurrentDirectory, string.Empty);
            File.WriteAllText(onPath, string.Empty);

            var resolved = OutputConverter.TryResolveExecutable(
                "ffmpeg",
                isWindows: true,
                processPath: null,
                currentDirectory: currentDir,
                pathEnvironment: pathDir
            );

            resolved.ShouldBe(fromCurrentDirectory);
        }
        finally
        {
            DeleteTempDirectory(tempRoot);
        }
    }

    [Test]
    public void GetRasterConversionStrategy_SelectsExpectedPipelines()
    {
        OutputConverter.GetRasterConversionStrategy(
                "output.png",
                ffmpegSupportsSvgInput: true,
                resvgAvailable: true
            )
            .ShouldBe(RasterConversionStrategy.DirectSvgWithFfmpeg);
        OutputConverter.GetRasterConversionStrategy(
                "output.png",
                ffmpegSupportsSvgInput: false,
                resvgAvailable: true
            )
            .ShouldBe(RasterConversionStrategy.ResvgPngOnly);
        OutputConverter.GetRasterConversionStrategy(
                "output.jpg",
                ffmpegSupportsSvgInput: false,
                resvgAvailable: true
            )
            .ShouldBe(RasterConversionStrategy.ResvgThenFfmpeg);
        OutputConverter.GetRasterConversionStrategy(
                "output.gif",
                ffmpegSupportsSvgInput: false,
                resvgAvailable: false
            )
            .ShouldBeNull();
    }

    [Test]
    public void GetVideoFrameExtension_UsesPngWhenResvgIsAvailable()
    {
        OutputConverter.GetVideoFrameExtension(useResvg: true).ShouldBe("png");
        OutputConverter.GetVideoFrameExtension(useResvg: false).ShouldBe("svg");
    }

    [Test]
    public void HelpOutputEnablesLibrsvg_DetectsBuildFlag()
    {
        OutputConverter.HelpOutputEnablesLibrsvg("configuration: --enable-gpl --enable-librsvg")
            .ShouldBeTrue();
        OutputConverter.HelpOutputEnablesLibrsvg("configuration: --enable-gpl").ShouldBeFalse();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"c2s-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
