using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class PtyCommandLineTests
{
    [Test]
    public void CmdPayloadKeepsInnerQuotesVerbatim()
    {
        var quoted = PtyCommandLine.QuoteArgs("cmd.exe", ["/d", "/s", "/c", "echo \"hello quoted\""]);

        quoted[^1].ShouldBe("\"echo \"hello quoted\"\"");
    }

    [Test]
    public void CmdPayloadWithoutQuotesIsPlainWrapped()
    {
        var quoted = PtyCommandLine.QuoteArgs("cmd.exe", ["/d", "/s", "/c", "echo hello"]);

        quoted[^1].ShouldBe("\"echo hello\"");
    }

    [Test]
    public void NonPayloadArgsKeepCRuntimeQuoting()
    {
        var quoted = PtyCommandLine.QuoteArgs("cmd.exe", ["/k"]);

        quoted.ShouldBe(["/k"]);
    }

    [Test]
    public void NonCmdAppsAreUnaffected()
    {
        var quoted = PtyCommandLine.QuoteArgs("/bin/sh", ["-c", "echo \"hi\""]);

        quoted.ShouldBe(["-c", "\"echo \\\"hi\\\"\""]);
    }
}
