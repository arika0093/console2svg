using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        session.Events.Count.ShouldBe(3);
        session.Events[0].Time.ShouldBe(0d);
        session.Events[0].Data.ShouldBe("\x1b[2J\x1b[H");
        session.Events[1].Data.ShouldBe("hello\n");
        session.Events[1].Time.ShouldBe(0.5d);
        session.Events[2].Time.ShouldBe(1.0d);
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
            <!-- c2s:: -o x.svg --
            echo setup
            ---
            echo capture
            -->
            """
        );

        var script = BatchExecutor.BuildScript(job, "/tmp/setup.log", isWindows: false);

        script.ShouldContain("set -e");
        script.ShouldContain("{ echo setup ; } > \"/tmp/setup.log\" 2>&1");
        script.ShouldContain("printf '__C2S_CAPTURE_START__\\n'");
        script.ShouldContain("echo capture");
        script.IndexOf("__C2S_CAPTURE_START__").ShouldBeLessThan(script.IndexOf("echo capture"));
    }

    [Test]
    public void BuildScriptOmitsSetupWrapperWhenEmpty()
    {
        var job = ParseSingle(
            """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o x.svg -->
            """
        );

        var script = BatchExecutor.BuildScript(job, null, isWindows: false);

        script.ShouldNotContain("setup.log");
        script.ShouldContain("echo hi");
    }

    [Test]
    public void ResolveOutputRejectsEscape()
    {
        var assets = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "c2s-assets"));
        BatchExecutor.ResolveOutput(assets, "../evil.svg").ShouldBeNull();
        var ok = BatchExecutor.ResolveOutput(assets, "sub/x.svg");
        ok.ShouldNotBeNull();
        ok!.ShouldContain("sub");
    }

    [Test]
    public async Task CacheHitMatchesSidecar()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "x.svg");
            await File.WriteAllTextAsync(output, "<svg/>");
            await File.WriteAllTextAsync(output + ".sha256", "abc123\n");

            (
                await BatchExecutor.IsCacheHitAsync(output, "abc123", CancellationToken.None)
            ).ShouldBeTrue();
            (
                await BatchExecutor.IsCacheHitAsync(output, "stale", CancellationToken.None)
            ).ShouldBeFalse();
            (
                await BatchExecutor.IsCacheHitAsync(
                    Path.Combine(dir, "missing.svg"),
                    "abc123",
                    CancellationToken.None
                )
            ).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static BatchParsedJob ParseSingle(string md)
    {
        var result = BatchMarkdown.Parse(md, "guide.md");
        result.Errors.ShouldBeEmpty();
        result.Jobs.Count.ShouldBe(1);
        return result.Jobs[0];
    }
}
