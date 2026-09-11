using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Recording;

public sealed class ReplayExecutorTests
{
    [Test]
    public async Task WaitForDelayedTerminalOutputThenWritesUnicodeAndKey()
    {
        var document = new ReplayDocumentV2
        {
            Steps =
            [
                new ReplayStep { WaitFor = new ReplayWaitFor { Text = "ready" } },
                new ReplayStep { Input = "こんにちは", Interval = "1ms" },
                new ReplayStep { Key = "Ctrl+C" },
            ],
        };
        var screen = new ReplayScreenObserver(40, 4);
        await using var input = new MemoryStream();

        var execution = ReplayExecutor.ExecuteAsync(document, input, screen, CancellationToken.None);
        await Task.Delay(20);
        screen.ProcessOutput("\x1b[32mready\x1b[0m");
        await execution;

        Encoding.UTF8.GetString(input.ToArray()).ShouldBe("こんにちは\u0003");
    }

    [Test]
    public async Task WaitForTimeoutIncludesAReadableScreenSnapshot()
    {
        var document = new ReplayDocumentV2
        {
            Defaults = new ReplayDefaults { Timeout = "30ms" },
            Steps = [new ReplayStep { WaitFor = new ReplayWaitFor { Text = "missing" } }],
        };
        var screen = new ReplayScreenObserver(20, 2);
        screen.ProcessOutput("shown");
        await using var input = new MemoryStream();

        try
        {
            await ReplayExecutor.ExecuteAsync(document, input, screen, CancellationToken.None);
            throw new InvalidOperationException("Expected replay timeout.");
        }
        catch (ReplayTimeoutException exception)
        {
            exception.Message.ShouldContain("shown");
            exception.Message.ShouldContain("30ms");
        }
    }

    [Test]
    public async Task ReplayLevelTimeoutCancelsAnOutstandingAction()
    {
        var document = new ReplayDocumentV2
        {
            Timeout = "30ms",
            Defaults = new ReplayDefaults { Timeout = "10s" },
            Steps = [new ReplayStep { WaitFor = new ReplayWaitFor { Text = "never" } }],
        };
        await using var input = new MemoryStream();

        try
        {
            await ReplayExecutor.ExecuteAsync(
                document,
                input,
                new ReplayScreenObserver(20, 2),
                CancellationToken.None
            );
            throw new InvalidOperationException("Expected replay timeout.");
        }
        catch (ReplayTimeoutException exception)
        {
            exception.Message.ShouldContain("Replay timed out after 30ms");
        }
    }

    [Test]
    public async Task CancellationIsNotRewrittenAsReplayTimeout()
    {
        var document = new ReplayDocumentV2
        {
            Defaults = new ReplayDefaults { Timeout = "10s" },
            Steps = [new ReplayStep { WaitFor = new ReplayWaitFor { Text = "never" } }],
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var input = new MemoryStream();

        try
        {
            await ReplayExecutor.ExecuteAsync(
                document,
                input,
                new ReplayScreenObserver(20, 2),
                cancellation.Token
            );
            throw new InvalidOperationException("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected: caller cancellation has priority over an action timeout.
        }
    }

    [Test]
    public void NormalizedScreenTextOmitsWideContinuationAndCanIncludeScrollback()
    {
        var emulator = new TerminalEmulator(6, 2, Theme.Resolve("dark"));
        emulator.Process("\x1b[31mAA\x1b[0m\r\nBB\r\n✅C");

        emulator.Buffer.GetNormalizedText().ShouldBe("BB\n✅C");
        emulator.Buffer.GetNormalizedText(includeScrollback: true).ShouldBe("AA\nBB\n✅C");
    }
}
