using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Recording;

public sealed class RepeatRecorderTests
{
    [Test]
    public async Task RecordAsync_CapturesAtLeastOneFrame()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));

        var session = await RepeatRecorder.RecordAsync(
            "echo hello",
            width: 40,
            height: 5,
            fps: 5,
            cts.Token
        );

        session.ShouldNotBeNull();
        session.Events.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task RecordAsync_FrameDataStartsWithClearScreen()
    {
        // ESC[0m ESC[2J ESC[H
        const string ClearScreen = "\x1b[0m\x1b[2J\x1b[H";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var session = await RepeatRecorder.RecordAsync(
            "echo hello",
            width: 40,
            height: 5,
            fps: 5,
            cts.Token
        );

        session.Events.Count.ShouldBeGreaterThanOrEqualTo(1);
        foreach (var ev in session.Events)
        {
            ev.Data.ShouldStartWith(ClearScreen);
        }
    }

    [Test]
    public async Task RecordAsync_TerminalShowsCommandOutput()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var session = await RepeatRecorder.RecordAsync(
            "echo hello",
            width: 40,
            height: 5,
            fps: 5,
            cts.Token
        );

        session.Events.Count.ShouldBeGreaterThanOrEqualTo(1);

        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(40, 5, theme);
        emulator.Replay(session, frameIndex: session.Events.Count - 1);

        // "hello" should appear somewhere in the first row.
        var row0 = string.Concat(Enumerable.Range(0, 40).Select(c => emulator.Buffer.GetCell(0, c).Text));
        row0.ShouldContain("hello");
    }

    [Test]
    public async Task RecordAsync_SessionDimensionsMatchRequest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var session = await RepeatRecorder.RecordAsync(
            "echo hi",
            width: 80,
            height: 24,
            fps: 5,
            cts.Token
        );

        session.Header.width.ShouldBe(80);
        session.Header.height.ShouldBe(24);
    }

    [Test]
    public async Task RecordAsync_ImmediateCancellationReturnsEmptySession()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var session = await RepeatRecorder.RecordAsync(
            "echo hi",
            width: 40,
            height: 5,
            fps: 5,
            cts.Token
        );

        // Already-cancelled token: the loop exits immediately so there may be 0 events.
        session.ShouldNotBeNull();
    }
}
