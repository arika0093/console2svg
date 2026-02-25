using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Recording;

public sealed class PipeRecorderTests
{
    [Test]
    public async Task RecordAsync_NormalizesLfToCrLf()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("A\nB"));

        var session = await PipeRecorder.RecordAsync(
            input,
            width: 8,
            height: 2,
            CancellationToken.None
        );

        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);
        emulator.Replay(session, frameIndex: session.Events.Count - 1);

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("B");
    }
}
