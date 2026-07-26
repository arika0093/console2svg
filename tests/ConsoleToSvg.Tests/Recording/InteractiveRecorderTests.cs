using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Recording;

public sealed class InteractiveRecorderTests
{
    private static readonly byte[] ScreenshotKey = Encoding.ASCII.GetBytes("\u001b[21~");
    private static readonly byte[] RecordingKey = Encoding.ASCII.GetBytes("\u001b[20~");

    [Test]
    public void F9AndF10AreConsumedAsRecordingAndScreenshotActions()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey);
        var forwarded = new List<byte>();

        Process(router, RecordingKey, forwarded).ShouldBe(InteractiveInputAction.ToggleRecording);
        forwarded.ShouldBeEmpty();
        Process(router, ScreenshotKey, forwarded).ShouldBe(InteractiveInputAction.Screenshot);
        forwarded.ShouldBeEmpty();
    }

    [Test]
    public void NonCaptureVtSequencesAreForwardedUnchangedEvenWhenSplitAcrossReads()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey);
        var forwarded = new List<byte>();
        var cursorUp = Encoding.ASCII.GetBytes("\u001b[A");

        foreach (var value in cursorUp)
        {
            router.Process(value, forwarded).ShouldBe(InteractiveInputAction.None);
        }

        forwarded.ToArray().ShouldBe(cursorUp);
    }

    [Test]
    public void CtrlLIsForwardedWithoutInteractiveRecorderIntervention()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey);
        var forwarded = new List<byte>();

        router.Process(0x0c, forwarded).ShouldBe(InteractiveInputAction.None);

        forwarded.ToArray().ShouldBe(new byte[] { 0x0c });
    }

    [Test]
    public void SgrMouseReportsAreDiscarded()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey);
        var forwarded = new List<byte>();

        foreach (var value in Encoding.ASCII.GetBytes("\u001b[<35;10;5M"))
        {
            router.Process(value, forwarded);
        }

        forwarded.ShouldBeEmpty();
    }

    [Test]
    public void CtrlDRequestsInteractiveExit()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey);
        var forwarded = new List<byte>();

        router.Process(0x04, forwarded).ShouldBe(InteractiveInputAction.Exit);
        forwarded.ToArray().ShouldBe(new byte[] { 0x04 });
    }

    [Test]
    public async Task ExitedInteractiveChildIsObservedWithoutHanging()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var cmd = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe"
        );
        using var connection = await NativePty.SpawnAsync(
            new NativePtyOptions
            {
                Name = "console2svg-test",
                Cols = 80,
                Rows = 24,
                Cwd = Environment.CurrentDirectory,
                App = cmd,
                Args = ["/d", "/c", "exit"],
            },
            CancellationToken.None
        );

        var stopwatch = Stopwatch.StartNew();
        while (!connection.WaitForExit(50) && stopwatch.Elapsed < TimeSpan.FromSeconds(5)) { }

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void CompleteRecordingAppendsTheFinalScreenAfterTheLastInput()
    {
        var emulator = new TerminalEmulator(20, 2, Theme.Resolve("dark"));
        emulator.Process("dotnet --version\r\n");
        var frames = new List<TerminalFrame> { new(0d, emulator.Buffer.Clone()) };
        emulator.Process("10.0.201");

        var capture = InteractiveRecorder.CompleteRecording(frames, 1d, emulator.Buffer);

        capture.Frames.Count.ShouldBe(2);
        GetRowText(capture.Frames[^1].Buffer, 1).ShouldBe("10.0.201");
    }

    [Test]
    public void HostFilterKeepsClearScreenSequencesWhileRemovingInputModes()
    {
        var filter = new InteractiveRecorder.HostTerminalSequenceFilter();

        var result = filter.Filter("\u001b[?9001h\u001b[?1004h\u001b[2J\u001b[H");

        result.ShouldBe("\u001b[2J\u001b[H");
    }

    private static InteractiveInputAction Process(
        InteractiveInputRouter router,
        IEnumerable<byte> values,
        List<byte> forwarded
    )
    {
        var action = InteractiveInputAction.None;
        foreach (var value in values)
        {
            action = router.Process(value, forwarded);
        }

        return action;
    }

    private static string GetRowText(ScreenBuffer buffer, int row)
    {
        var text = new StringBuilder();
        for (var col = 0; col < buffer.Width; col++)
        {
            text.Append(buffer.GetCell(row, col).Text);
        }

        return text.ToString().TrimEnd();
    }
}
