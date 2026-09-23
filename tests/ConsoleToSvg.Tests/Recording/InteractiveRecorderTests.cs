using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using Porta.Pty;

namespace ConsoleToSvg.Tests.Recording;

public sealed class InteractiveRecorderTests
{
    private static readonly byte[] ScreenshotKey = Encoding.ASCII.GetBytes("\u001b[21~");
    private static readonly byte[] RecordingKey = Encoding.ASCII.GetBytes("\u001b[20~");
    private static readonly byte[] PauseKey = Encoding.ASCII.GetBytes("\u001b[24~");

    [Test]
    public void F9F10AndF12AreConsumedAsInteractiveCaptureActions()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        Process(router, RecordingKey, forwarded).ShouldBe(InteractiveInputAction.ToggleRecording);
        forwarded.ShouldBeEmpty();
        Process(router, ScreenshotKey, forwarded).ShouldBe(InteractiveInputAction.Screenshot);
        forwarded.ShouldBeEmpty();
        Process(router, PauseKey, forwarded).ShouldBe(InteractiveInputAction.TogglePause);
        forwarded.ShouldBeEmpty();
    }

    [Test]
    public void NonCaptureVtSequencesAreForwardedUnchangedEvenWhenSplitAcrossReads()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
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
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        router.Process(0x0c, forwarded).ShouldBe(InteractiveInputAction.None);

        forwarded.ToArray().ShouldBe(new byte[] { 0x0c });
    }

    [Test]
    public void SgrMouseReportsAreDiscarded()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        foreach (var value in Encoding.ASCII.GetBytes("\u001b[<35;10;5M"))
        {
            router.Process(value, forwarded);
        }

        forwarded.ShouldBeEmpty();
    }

    [Test]
    public void SgrMouseReportsAreForwardedWhenMousePassthroughIsEnabled()
    {
        var router = new InteractiveInputRouter(
            ScreenshotKey,
            RecordingKey,
            PauseKey,
            mousePassthrough: true
        );
        var forwarded = new List<byte>();
        var report = Encoding.ASCII.GetBytes("\u001b[<35;10;5M");

        foreach (var value in report)
        {
            router.Process(value, forwarded).ShouldBe(InteractiveInputAction.None);
        }

        forwarded.ToArray().ShouldBe(report);
    }

    [Test]
    public void CtrlDRequestsInteractiveExit()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        router.Process(0x04, forwarded).ShouldBe(InteractiveInputAction.Exit);
        forwarded.ToArray().ShouldBe(new byte[] { 0x04 });
    }

    [Test]
    public void CtrlDRequestsExitWhenCaptureControlsAreDisabled()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        router
            .Process(0x04, forwarded, captureControlsEnabled: false)
            .ShouldBe(InteractiveInputAction.Exit);
        forwarded.ToArray().ShouldBe(new byte[] { 0x04 });
    }

    [Test]
    public void CaptureKeysAreForwardedWhenCaptureControlsAreDisabled()
    {
        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        foreach (var value in ScreenshotKey)
        {
            router
                .Process(value, forwarded, captureControlsEnabled: false)
                .ShouldBe(InteractiveInputAction.None);
        }

        forwarded.ToArray().ShouldBe(ScreenshotKey);
    }

    [Test]
    public void WindowsExtendedRightArrowIsForwardedAsVtInput()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var router = new InteractiveInputRouter(ScreenshotKey, RecordingKey, PauseKey);
        var forwarded = new List<byte>();

        router
            .Process(0xE0, forwarded, captureControlsEnabled: false)
            .ShouldBe(InteractiveInputAction.None);
        router
            .Process(0x4D, forwarded, captureControlsEnabled: false)
            .ShouldBe(InteractiveInputAction.None);

        forwarded.ToArray().ShouldBe(Encoding.ASCII.GetBytes("\u001b[C"));
    }

    [Test]
    public void WindowsVtConsoleInputIsAlreadyUtf8()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // ENABLE_VIRTUAL_TERMINAL_INPUT supplies UTF-8 bytes to ReadFile. The
        // forwarding path must preserve both Japanese text and VT sequences;
        // decoding these bytes as CP932 causes the reported mojibake.
        var input = Encoding.UTF8.GetBytes("あ\u001b[A");
        input.ShouldBe(new byte[] { 0xE3, 0x81, 0x82, 0x1B, 0x5B, 0x41 });
    }

    [Test]
    public void NestedShellDetectionIgnoresConhostButSeesUserShells()
    {
        InteractiveRecorder.IsShellChildProcess("conhost.exe").ShouldBeFalse();
        InteractiveRecorder.IsShellChildProcess("CONHOST.EXE").ShouldBeFalse();
        InteractiveRecorder.IsShellChildProcess("conhost").ShouldBeFalse();
        InteractiveRecorder.IsShellChildProcess("wsl.exe").ShouldBeTrue();
        InteractiveRecorder.IsShellChildProcess("powershell.exe").ShouldBeTrue();
        InteractiveRecorder.IsShellChildProcess("cmd.exe").ShouldBeTrue();
        InteractiveRecorder.IsShellChildProcess(null).ShouldBeFalse();
        InteractiveRecorder.IsShellChildProcess("").ShouldBeFalse();
        InteractiveRecorder.IsShellChildProcess("  ").ShouldBeFalse();
    }

    [Test]
    public async Task CtrlDNestedShellDetectionSeesTopLevelPtyWithoutChildren()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var cmd = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe"
        );
        // `pause` keeps the root shell alive without needing stdin and never
        // spawns children. Only the ConPTY host (conhost.exe, excluded) may be
        // attached, so this must read as "no nested shell" the whole time.
        using var connection = await PtyProvider.SpawnAsync(
            new PtyOptions
            {
                Name = "console2svg-test",
                Cols = 80,
                Rows = 24,
                Cwd = Environment.CurrentDirectory,
                App = cmd,
                CommandLine = ["/d", "/c", "pause"],
            },
            CancellationToken.None
        );
        (connection.Pid > 0).ShouldBeTrue();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            InteractiveRecorder
                .HasNestedChildProcesses(connection.Pid)
                .ShouldBeFalse();
            await Task.Delay(100, CancellationToken.None);
        }
    }

    [Test]
    public void CtrlDNestedShellDetectionSeesOwnChildProcess()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var cmd = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe"
        );
        using var child = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = cmd,
                Arguments = "/d /c ping -n 30 127.0.0.1 >nul",
                CreateNoWindow = true,
                UseShellExecute = false,
            }
        );
        try
        {
            (child is not null).ShouldBeTrue();
            var detected = false;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (InteractiveRecorder.HasNestedChildProcesses(Environment.ProcessId))
                {
                    detected = true;
                    break;
                }
                Thread.Sleep(100);
            }
            detected.ShouldBeTrue();
        }
        finally
        {
            try
            {
                child?.Kill();
                child?.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
            }
            catch
            {
                // Best-effort cleanup; the ping exits on its own within seconds.
            }
        }
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
        using var connection = await PtyProvider.SpawnAsync(
            new PtyOptions
            {
                Name = "console2svg-test",
                Cols = 80,
                Rows = 24,
                Cwd = Environment.CurrentDirectory,
                App = cmd,
                CommandLine = ["/d", "/c", "exit"],
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

    [Test]
    public void HostFilterRemovesInputModesSplitAcrossOutputReads()
    {
        var filter = new InteractiveRecorder.HostTerminalSequenceFilter();

        filter.Filter("\u001b[?10").ShouldBeEmpty();
        filter.Filter("06hready").ShouldBe("ready");
    }

    [Test]
    public void HostFilterRemovesCombinedInputModes()
    {
        var filter = new InteractiveRecorder.HostTerminalSequenceFilter();

        var result = filter.Filter("\u001b[?1000;1006hready");

        result.ShouldBe("ready");
    }

    [Test]
    public void HostFilterPassesMouseModesWhenMousePassthroughIsEnabled()
    {
        var filter = new InteractiveRecorder.HostTerminalSequenceFilter(mousePassthrough: true);

        var result = filter.Filter("\u001b[?1000;1006hready");

        result.ShouldBe("\u001b[?1000;1006hready");
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
