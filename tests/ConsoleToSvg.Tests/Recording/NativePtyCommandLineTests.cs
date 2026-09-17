using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class NativePtyCommandLineTests
{
    [Test]
    public void CmdPayloadKeepsInnerQuotesVerbatim()
    {
        var line = NativePtyWindows.BuildCommandLine(
            "cmd.exe",
            ["/d", "/s", "/c", "echo \"hello quoted\""]
        );

        line.ShouldBe("cmd.exe /d /s /c \"echo \"hello quoted\"\"");
    }

    [Test]
    public void CmdPayloadWithoutQuotesIsPlainWrapped()
    {
        var line = NativePtyWindows.BuildCommandLine(
            "cmd.exe",
            ["/d", "/s", "/c", "echo hello"]
        );

        line.ShouldBe("cmd.exe /d /s /c \"echo hello\"");
    }

    [Test]
    public void NonPayloadArgsKeepCRuntimeQuoting()
    {
        var line = NativePtyWindows.BuildCommandLine(
            "cmd.exe",
            ["/k"]
        );

        line.ShouldBe("cmd.exe /k");
    }

    [Test]
    public void NonCmdAppsAreUnaffected()
    {
        var line = NativePtyWindows.BuildCommandLine(
            "/bin/sh",
            ["-c", "echo \"hi\""]
        );

        line.ShouldBe("/bin/sh -c \"echo \\\"hi\\\"\"");
    }
}
