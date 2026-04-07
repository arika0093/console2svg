using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Raster;

[SuppressMessage("Interoperability", "CA1416", Justification = "These shell-script-based ffmpeg shim tests run in the Linux test environment.")]
public sealed class RasterImageOutputTests
{
    private static readonly SemaphoreSlim EnvironmentLock = new(initialCount: 1, maxCount: 1);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Test]
    public async Task PngOutputDoesNotRequireFfmpeg()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("console2svg-raster-");

        try
        {
            var castPath = Path.Combine(tempDirectory.FullName, "input.cast");
            var outputPath = Path.Combine(tempDirectory.FullName, "output.png");

            var session = CreateSession();
            await AsciicastWriter.WriteToFileAsync(castPath, session, CancellationToken.None);

            var exitCode = await InvokeProgramWithPathAsync(
                tempDirectory.FullName,
                "--in",
                castPath,
                "--out",
                outputPath
            );

            exitCode.ShouldBe(0);
            File.Exists(outputPath).ShouldBeTrue();

            var pngBytes = await File.ReadAllBytesAsync(outputPath);
            pngBytes.Length.ShouldBeGreaterThanOrEqualTo(PngSignature.Length);
            pngBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature).ShouldBeTrue();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task JpegOutputUsesDirectSvgWhenFfmpegSupportsLibrsvg()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("console2svg-raster-direct-");

        try
        {
            var castPath = Path.Combine(tempDirectory.FullName, "input.cast");
            var outputPath = Path.Combine(tempDirectory.FullName, "output.jpg");
            var argsLogPath = Path.Combine(tempDirectory.FullName, "ffmpeg.args");

            await AsciicastWriter.WriteToFileAsync(castPath, CreateSession(), CancellationToken.None);
            await CreateFakeFfmpegAsync(
                Path.Combine(tempDirectory.FullName, "ffmpeg"),
                argsLogPath,
                supportsLibrsvg: true
            );

            var exitCode = await InvokeProgramWithPathAsync(
                tempDirectory.FullName,
                "--in",
                castPath,
                "--out",
                outputPath
            );

            exitCode.ShouldBe(0);

            var ffmpegArgs = await File.ReadAllTextAsync(argsLogPath);
            ffmpegArgs.ShouldContain(".svg");
            ffmpegArgs.ShouldNotContain(".png");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task JpegOutputUsesResvgFallbackWhenFfmpegLacksLibrsvg()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("console2svg-raster-fallback-");

        try
        {
            var castPath = Path.Combine(tempDirectory.FullName, "input.cast");
            var outputPath = Path.Combine(tempDirectory.FullName, "output.jpg");
            var argsLogPath = Path.Combine(tempDirectory.FullName, "ffmpeg.args");

            await AsciicastWriter.WriteToFileAsync(castPath, CreateSession(), CancellationToken.None);
            await CreateFakeFfmpegAsync(
                Path.Combine(tempDirectory.FullName, "ffmpeg"),
                argsLogPath,
                supportsLibrsvg: false
            );

            var exitCode = await InvokeProgramWithPathAsync(
                tempDirectory.FullName,
                "--in",
                castPath,
                "--out",
                outputPath
            );

            exitCode.ShouldBe(0);

            var ffmpegArgs = await File.ReadAllTextAsync(argsLogPath);
            ffmpegArgs.ShouldContain(".png");
            ffmpegArgs.ShouldNotContain(".svg");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task VideoOutputUsesSvgFramesWhenFfmpegSupportsLibrsvg()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("console2svg-video-direct-");

        try
        {
            var castPath = Path.Combine(tempDirectory.FullName, "input.cast");
            var outputPath = Path.Combine(tempDirectory.FullName, "output.mp4");
            var argsLogPath = Path.Combine(tempDirectory.FullName, "ffmpeg.args");

            await AsciicastWriter.WriteToFileAsync(castPath, CreateSession(), CancellationToken.None);
            await CreateFakeFfmpegAsync(
                Path.Combine(tempDirectory.FullName, "ffmpeg"),
                argsLogPath,
                supportsLibrsvg: true
            );

            var exitCode = await InvokeProgramWithPathAsync(
                tempDirectory.FullName,
                "-v",
                "--in",
                castPath,
                "--out",
                outputPath
            );

            exitCode.ShouldBe(0);

            var ffmpegArgs = await File.ReadAllTextAsync(argsLogPath);
            ffmpegArgs.ShouldContain("frame-%04d.svg");
            ffmpegArgs.ShouldNotContain("frame-%04d.png");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task VideoOutputUsesPngFramesWhenFfmpegLacksLibrsvg()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("console2svg-video-fallback-");

        try
        {
            var castPath = Path.Combine(tempDirectory.FullName, "input.cast");
            var outputPath = Path.Combine(tempDirectory.FullName, "output.mp4");
            var argsLogPath = Path.Combine(tempDirectory.FullName, "ffmpeg.args");

            await AsciicastWriter.WriteToFileAsync(castPath, CreateSession(), CancellationToken.None);
            await CreateFakeFfmpegAsync(
                Path.Combine(tempDirectory.FullName, "ffmpeg"),
                argsLogPath,
                supportsLibrsvg: false
            );

            var exitCode = await InvokeProgramWithPathAsync(
                tempDirectory.FullName,
                "-v",
                "--in",
                castPath,
                "--out",
                outputPath
            );

            exitCode.ShouldBe(0);

            var ffmpegArgs = await File.ReadAllTextAsync(argsLogPath);
            ffmpegArgs.ShouldContain("frame-%04d.png");
            ffmpegArgs.ShouldNotContain("frame-%04d.svg");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static RecordingSession CreateSession()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "A");
        session.AddEvent(0.10, "B");
        return session;
    }

    private static async Task CreateFakeFfmpegAsync(
        string ffmpegPath,
        string argsLogPath,
        bool supportsLibrsvg
    )
    {
        var helpLine = supportsLibrsvg
            ? "configuration: --enable-gpl --enable-librsvg"
            : "configuration: --enable-gpl";

        await File.WriteAllTextAsync(
            ffmpegPath,
            "#!/bin/bash\n"
                + "if [ \"$1\" = \"-h\" ]; then\n"
                + $"  echo '{helpLine}'\n"
                + "  exit 0\n"
                + "fi\n"
                + $"printf '%s\\n' \"$@\" > \"{argsLogPath}\"\n"
                + ": > \"${@: -1}\"\n",
            CancellationToken.None
        );

        File.SetUnixFileMode(
            ffmpegPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
    }

    private static async Task<int> InvokeProgramWithPathAsync(string pathOverride, params string[] args)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        await EnvironmentLock.WaitAsync();
        try
        {
            Environment.SetEnvironmentVariable("PATH", pathOverride);
            return await InvokeProgramAsync(args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            EnvironmentLock.Release();
        }
    }

    private static async Task<int> InvokeProgramAsync(params string[] args)
    {
        var programType = typeof(OptionParser).Assembly.GetType("ConsoleToSvg.Program")
            ?? throw new InvalidOperationException("ConsoleToSvg.Program was not found.");
        var mainMethod = programType.GetMethod(
            "Main",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException("ConsoleToSvg.Program.Main was not found.");

        var task = (Task<int>?)mainMethod.Invoke(null, [args])
            ?? throw new InvalidOperationException("ConsoleToSvg.Program.Main did not return Task<int>.");

        return await task.ConfigureAwait(false);
    }
}
