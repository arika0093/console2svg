using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class PtyRecorderTests
{
    [Test]
    public async Task RecordAsync_CapturesOutput()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (Environment.OSVersion.Version.Major < 10)
            {
                return;
            }
        }

        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo PTY_TEST"
            : "printf 'PTY_TEST\\n'";

        var session = await PtyRecorder.RecordAsync(
            command,
            width: 80,
            height: 24,
            CancellationToken.None,
            logger: null,
            forwardToConsole: false
        );

        var text = string.Concat(session.Events.Select(e => e.Data));
        text.ShouldContain("PTY_TEST");
    }
}
