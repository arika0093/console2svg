using System.IO;
using System.Linq;
using ConsoleToSvg.Batch;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchExecutorTests
{
    [Test]
    public void TrimDropsSetupAndRebasesTimeline()
    {
        var session = new RecordingSession(width: 80, height: 24);
        session.AddEvent(0.5, "setup output\n");
        session.AddEvent(1.0, "prefix __C2S_CAPTURE_START__ suffix\n");
        session.AddEvent(1.5, "hello\n");
        session.AddEvent(2.0, "world\n");

        BatchExecutor.TryTrimBeforeMarker(session).ShouldBeTrue();

        session.Events.Count.ShouldBe(4);
        session.Events[0].Time.ShouldBe(0d);
        session.Events[0].Data.ShouldBe("\x1b[2J\x1b[H");
        session.Events[1].Data.ShouldBe(" suffix\n");
        session.Events[1].Time.ShouldBe(0d);
        session.Events[2].Data.ShouldBe("hello\n");
        session.Events[2].Time.ShouldBe(0.5d);
        session.Events[3].Time.ShouldBe(1.0d);
    }

    [Test]
    public void TrimKeepsCaptureOutputCoalescedWithMarker()
    {
        var session = new RecordingSession(width: 80, height: 24);
        session.AddEvent(0.5, "setup output\n");
        session.AddEvent(1.0, "__C2S_CAPTURE_START__\r\nhi\r\n");

        BatchExecutor.TryTrimBeforeMarker(session).ShouldBeTrue();

        session.Events.Count.ShouldBe(2);
        session.Events[0].Data.ShouldBe("\x1b[2J\x1b[H");
        session.Events[1].Data.ShouldBe("\r\nhi\r\n");
        session.Events[1].Time.ShouldBe(0d);
    }

    [Test]
    public void TrimWithoutMarkerFails()
    {
        var session = new RecordingSession(width: 80, height: 24);
        session.AddEvent(0.5, "setup output\n");

        BatchExecutor.TryTrimBeforeMarker(session).ShouldBeFalse();
        session.Events.Count.ShouldBe(1);
    }

    [Test]
    public void BuildScriptWrapsSetupUnix()
    {
        var job = ParseSingle(
            """
            <!-- c2s:: -o x.svg
            setup: echo setup
            capture: echo capture
            -->
            """
        );

        var script = BatchExecutor.BuildScript(job, "/tmp/setup.log", isWindows: false);

        script.ShouldContain("set -e");
        script.ShouldContain("echo setup");
        script.ShouldContain("} > \"/tmp/setup.log\" 2>&1");
        script.ShouldContain("printf '__C2S_CAPTURE_START__\\n'");
        script.IndexOf("__C2S_CAPTURE_START__").ShouldBeLessThan(script.IndexOf("echo capture"));
    }

    [Test]
    public void BuildScriptKeepsClosingBraceAfterTrailingSetupComment()
    {
        var job = ParseSingle(
            """
            <!-- c2s:: -o x.svg
            setup: |
              echo ready
              # explanation
            capture: echo capture
            -->
            """
        );

        var script = BatchExecutor.BuildScript(job, "/tmp/setup.log", isWindows: false);
        var lines = script.Replace("\r\n", "\n").Split('\n');

        lines.ShouldContain("# explanation");
        lines.ShouldContain("} > \"/tmp/setup.log\" 2>&1");
    }

    [Test]
    public void BuildScriptStopsAfterFailedSetupOnWindows()
    {
        var job = ParseSingle(
            """
            <!-- c2s::
            setup: failing-command
            capture: echo capture
            -->
            """
        );

        var script = BatchExecutor.BuildScript(job, "C:/temp/setup.log", isWindows: true);

        script.ShouldContain("|| exit /b 1");
        script.IndexOf("|| exit /b 1").ShouldBeLessThan(script.IndexOf("__C2S_CAPTURE_START__"));
    }

    [Test]
    public void BuildWindowsScriptIsSingleLine()
    {
        var job = ParseSingle(
            """
            <!-- c2s:: -o x.svg
            setup: echo setup
            capture: |
              echo one
              echo two
            -->
            """
        );

        var script = BatchExecutor.BuildScript(job, "C:/temp/setup.log", isWindows: true);

        script.ShouldNotContain("\n");
        script.ShouldContain("( echo setup ) > \"C:/temp/setup.log\" 2>&1 || exit /b 1");
        script.ShouldContain("echo __C2S_CAPTURE_START__ & cls & echo one & echo two");
    }

    [Test]
    public void ResolveOutputRejectsEscape()
    {
        var output = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "c2s-assets"));

        BatchExecutor.ResolveOutput(output, "../evil.svg").ShouldBeNull();
        BatchExecutor.ResolveOutput(output, "sub/x.svg").ShouldNotBeNull();
    }

    [Test]
    public void ResolveMarkdownLinkUsesMarkdownDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "c2s-link-test");
        var markdown = Path.Combine(root, "docs", "guide.md");
        var expected = Path.GetFullPath(Path.Combine(root, "assets", "guide.svg"));

        var resolved = BatchExecutor.ResolveMarkdownLink(markdown, "../assets/guide.svg");

        resolved.ShouldBe(expected);
        BatchExecutor.ResolveMarkdownLink(markdown, "https://example.com/x.svg").ShouldBeNull();
    }

    [Test]
    public void GlobFiltersUseNormalizedInputRelativePaths()
    {
        BatchExecutor.MatchesFilter("reference/cli.md", "reference/**").ShouldBeTrue();
        BatchExecutor.MatchesFilter("reference/deep/cli.mdx", "reference/**").ShouldBeTrue();
        BatchExecutor.MatchesFilter("tutorial/cli.md", "reference/**").ShouldBeFalse();
        BatchExecutor.MatchesFilter("tutorial/cli.md", "**/cli.?d").ShouldBeTrue();
    }

    private static BatchParsedJob ParseSingle(string markdown)
    {
        var result = BatchMarkdown.Parse(markdown, "guide.md");
        result.Errors.ShouldBeEmpty();
        return result.Jobs.Single();
    }
}
