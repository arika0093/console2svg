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
    public void WindowsConsoleInputTranscodesToUtf8ForConPty()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Mirrors the transcode path in InteractiveRecorder input forwarding:
        // host bytes arrive in Console.InputEncoding (CP932 on Japanese
        // Windows) while ConPTY expects UTF-8. Use CP932 explicitly: under a
        // test runner stdin is redirected, so Console.InputEncoding is not
        // necessarily the console code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var hostEncoding = Encoding.GetEncoding(932);
        var decoder = hostEncoding.GetDecoder();
        var chars = new char[512];

        // "あ" in Shift_JIS must become U+3042 (UTF-8 E3 81 82), not U+FFFD.
        var shiftJisA = new byte[] { 0x82, 0xA0 };
        var charCount = decoder.GetChars(shiftJisA, 0, shiftJisA.Length, chars, 0, flush: false);
        charCount.ShouldBe(1);
        new string(chars, 0, charCount).ShouldBe("あ");
        Encoding.UTF8.GetBytes(chars, 0, charCount).ShouldBe(new byte[] { 0xE3, 0x81, 0x82 });

        // VT sequences (arrow keys) are ASCII and must round-trip unchanged.
        var vtUp = Encoding.ASCII.GetBytes("\u001b[A");
        charCount = decoder.GetChars(vtUp, 0, vtUp.Length, chars, 0, flush: false);
        Encoding.UTF8.GetBytes(chars, 0, charCount).ShouldBe(vtUp);
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
        using var connection = await NativePty.SpawnAsync(
            new NativePtyOptions
            {
                Name = "console2svg-test",
                Cols = 80,
                Rows = 24,
                Cwd = Environment.CurrentDirectory,
                App = cmd,
                Args = ["/d", "/c", "pause"],
            },
            CancellationToken.None
        );
        (connection.ProcessId > 0).ShouldBeTrue();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            InteractiveRecorder
                .HasNestedChildProcesses(connection.ProcessId)
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
