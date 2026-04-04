using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Conversion;
using ConsoleToSvg.Recording;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg.Tests.Conversion;

public sealed class OutputEmitterTests
{
    [Test]
    public async Task EmitAsyncUsesResvgForPngOutput()
    {
        using var tempDir = new TestTempDirectory();
        var logPath = System.IO.Path.Combine(tempDir.Path, "resvg.log");
        var outputPath = System.IO.Path.Combine(tempDir.Path, "output.png");
        var fakeResvg = tempDir.CreateExecutable(
            "resvg",
            "fake-resvg",
            logPath,
            writeSecondArgToOutput: true
        );

        using var scope = new EnvironmentScope();
        scope.Set(ToolResolver.ResvgEnvironmentVariable, fakeResvg);

        await OutputEmitter.EmitAsync(
            CreateSession(),
            new AppOptions
            {
                OutputPath = outputPath,
                Mode = OutputMode.Image,
            },
            CreateLogger(),
            CancellationToken.None
        );

        System.IO.File.Exists(outputPath).ShouldBeTrue();
        (await System.IO.File.ReadAllTextAsync(outputPath)).Trim().ShouldBe("fake-resvg");
        (await System.IO.File.ReadAllTextAsync(logPath)).ShouldContain("output.svg");
    }

    [Test]
    public async Task EmitAsyncUsesFfmpegForMp4OutputAndPreservesSaveFrames()
    {
        using var tempDir = new TestTempDirectory();
        var resvgLogPath = System.IO.Path.Combine(tempDir.Path, "resvg.log");
        var ffmpegLogPath = System.IO.Path.Combine(tempDir.Path, "ffmpeg.log");
        var outputPath = System.IO.Path.Combine(tempDir.Path, "output.mp4");
        var saveFramesDir = System.IO.Path.Combine(tempDir.Path, "frames");
        var fakeResvg = tempDir.CreateExecutable(
            "resvg",
            "fake-png-frame",
            resvgLogPath,
            writeSecondArgToOutput: true
        );
        var fakeFfmpeg = tempDir.CreateExecutable(
            "ffmpeg",
            "fake-mp4",
            ffmpegLogPath,
            writeLastArgToOutput: true
        );

        using var scope = new EnvironmentScope();
        scope.Set(ToolResolver.ResvgEnvironmentVariable, fakeResvg);
        scope.Set(ToolResolver.FfmpegEnvironmentVariable, fakeFfmpeg);

        await OutputEmitter.EmitAsync(
            CreateSession(),
            new AppOptions
            {
                OutputPath = outputPath,
                Mode = OutputMode.Video,
                VideoFps = 24.5,
                SaveFramesDir = saveFramesDir,
            },
            CreateLogger(),
            CancellationToken.None
        );

        System.IO.File.Exists(outputPath).ShouldBeTrue();
        (await System.IO.File.ReadAllTextAsync(outputPath)).Trim().ShouldBe("fake-mp4");
        (await System.IO.File.ReadAllTextAsync(ffmpegLogPath)).ShouldContain("-framerate 24.5");
        System.IO.Directory.Exists(saveFramesDir).ShouldBeTrue();
        System.IO.Directory.GetFiles(saveFramesDir, "frame-*.svg").Length.ShouldBeGreaterThan(0);
    }

    private static RecordingSession CreateSession()
    {
        var session = new RecordingSession(80, 24);
        session.AddEvent(0.0, "hello");
        session.AddEvent(0.2, "\r\nworld");
        return session;
    }

    private static ILogger CreateLogger() => LoggerFactory.Create(_ => { }).CreateLogger("test");
}
