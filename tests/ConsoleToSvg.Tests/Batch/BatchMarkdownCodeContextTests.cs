using System.Linq;
using ConsoleToSvg.Batch;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchMarkdownCodeContextTests
{
    [Test]
    public void MarkerLookingTextInMarkdownCodeContextsIsIgnored()
    {
        var markdown = """
                <!-- c2s:: -- echo indented -->

            `<!-- c2s:: -- echo inline -->`

            > ```markdown
            > <!-- c2s:: -- echo blockquote-fence -->
            > ```

            <pre>
            <!-- c2s:: -- echo html-pre -->
            </pre>

            <code><!-- c2s:: -- echo html-code --></code>

            <!-- c2s:: -- echo executable -->
            """;

        var result = BatchMarkdown.Parse(markdown, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs.Single().Capture.ShouldBe("echo executable");
    }

    [Test]
    public void ReplayOptionsRemainAvailableToBatchExecution()
    {
        var replayResult = BatchMarkdown.Parse(
            "<!-- c2s:: --replay input.json -- echo hi -->",
            "guide.md"
        );
        var replaySaveResult = BatchMarkdown.Parse(
            "<!-- c2s:: --replay-save captured.json -- echo hi -->",
            "guide.md"
        );

        replayResult.Errors.ShouldBeEmpty();
        replayResult.Jobs.Single().CaptureOptions.ReplayPath.ShouldBe("input.json");
        replaySaveResult.Errors.ShouldBeEmpty();
        replaySaveResult.Jobs.Single().CaptureOptions.ReplaySavePath.ShouldBe("captured.json");
    }
}
