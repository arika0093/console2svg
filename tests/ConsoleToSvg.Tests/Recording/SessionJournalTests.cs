using System;
using System.IO;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class SessionJournalTests
{
    [Test]
    public async Task JournalEntriesRoundTripAndUsePrivatePermissions()
    {
        var sessionId = $"s_{Guid.NewGuid():N}";
        var path = Path.Combine(SessionJournalStore.GetJournalRoot(), sessionId + ".jsonl");

        try
        {
            await SessionJournalStore.AppendAsync(
                sessionId,
                new SessionJournalEntry
                {
                    Kind = "action",
                    Name = "send",
                    Inputs = [new TerminalInput(TerminalInputKind.Text, "hello")],
                    At = DateTimeOffset.UtcNow,
                }
            );

            var entries = await SessionJournalStore.ReadAsync(sessionId);
            entries.Count.ShouldBe(1);
            entries[0].Name.ShouldBe("send");
            entries[0].Inputs![0].Value.ShouldBe("hello");
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
