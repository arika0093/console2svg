using System;
using System.IO;
using System.Threading.Tasks;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchIntegrationTests
{
    [Test]
    public async Task AutoOutputPreservesInputRelativeDirectoryAndRewritesMarkdown()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "reference", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                ```bash
                echo hello
                ```
                <!-- c2s:: -w 40 -h 5 -->
                """
            );

            var exitCode = await Program.Main(["batch", "markdown", "-i", docs, "-o", output]);

            exitCode.ShouldBe(0);
            File.Exists(Path.Combine(output, "reference", "guide-1.svg")).ShouldBeTrue();
            var rewritten = await File.ReadAllTextAsync(markdownPath);
            rewritten.ShouldContain("![echo hello](../../assets/reference/guide-1.svg)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExistingAutoOutputLinkRemainsTheOutputOwnerAfterMovingContent()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "hoge", "test.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                ```bash
                echo moved
                ```
                <!-- c2s:: -w 40 -h 5 -->
                ![old](../../assets/sample/test-7.svg)
                """
            );

            var exitCode = await Program.Main(["batch", "markdown", "-i", docs, "-o", output]);

            exitCode.ShouldBe(0);
            File.Exists(Path.Combine(output, "sample", "test-7.svg")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "hoge", "test-1.svg")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DryRunDoesNotCreateOutputOrRewriteMarkdown()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdownPath = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            const string markdown = """
                ```bash
                echo hello
                ```
                <!-- c2s:: -->
                """;
            await File.WriteAllTextAsync(markdownPath, markdown);

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
                "--dry-run",
            ]);

            exitCode.ShouldBe(0);
            Directory.Exists(output).ShouldBeFalse();
            (await File.ReadAllTextAsync(markdownPath)).ShouldBe(markdown);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DuplicateOutputProducersFailBeforeCreatingOutput()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdownPath = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <!-- c2s:: -o shared.svg -- echo first -->
                <!-- c2s:: -o shared.svg -- echo second -->
                """
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(1);
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TeardownRunsWhenSetupFails()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdownPath = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            var teardownMarker = Path.Combine(root, "teardown-ran.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                $"""
                <!-- c2s::
                setup: console2svg-command-that-does-not-exist
                capture: echo unreachable
                teardown: echo cleaned > "{teardownMarker.Replace('\\', '/')}"
                -->
                """
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(1);
            File.Exists(teardownMarker).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MissingSharedOutputReportsProducerExcludedByFilter()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(
                Path.Combine(docs, "producer.md"),
                "<!-- c2s:: -o shared/version.svg -- echo version -->"
            );
            await File.WriteAllTextAsync(
                Path.Combine(docs, "consumer.md"),
                "![shared output](../assets/shared/version.svg)"
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--filter",
                "consumer.md",
            ]);

            exitCode.ShouldBe(1);
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CaseOnlyOutputCollisionIsRejectedOnCaseInsensitivePlatforms()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = CreateTempDirectory();
        try
        {
            var markdownPath = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <!-- c2s:: -o Case.svg -- echo first -->
                <!-- c2s:: -o case.svg -- echo second -->
                """
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(1);
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RelativeBackgroundImageUsesMarkdownDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdownDirectory = Path.Combine(root, "docs", "nested");
            var markdownPath = Path.Combine(markdownDirectory, "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(markdownDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(markdownDirectory, "background.png"),
                "batch-background"
            );
            await File.WriteAllTextAsync(
                markdownPath,
                "<!-- c2s:: -o background.svg --background background.png -- echo background -->"
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(0);
            var svg = await File.ReadAllTextAsync(Path.Combine(output, "background.svg"));
            svg.ShouldContain(
                "data:image/png;base64,"
                    + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("batch-background"))
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RuntimeRelativePathsUseMarkdownDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownDirectory = Path.Combine(docs, "nested");
            var markdownPath = Path.Combine(markdownDirectory, "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(markdownDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(markdownDirectory, "replay.json"),
                """{"version":"1","totalDuration":10.0,"replay":[]}"""
            );
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <!-- c2s:: -o replay.svg --replay replay.json -- echo replay -->

                <!-- c2s:: -o cwd.svg --replay-save recordings/captured.json
                setup: echo setup > setup-relative.txt
                capture: echo capture > capture-relative.txt && echo capture
                teardown: echo teardown > teardown-relative.txt
                -->
                """
            );

            var exitCode = await Program.Main(["batch", "markdown", "-i", docs, "-o", output]);

            exitCode.ShouldBe(0);
            File.Exists(Path.Combine(output, "replay.svg")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "cwd.svg")).ShouldBeTrue();
            File.Exists(Path.Combine(markdownDirectory, "setup-relative.txt")).ShouldBeTrue();
            File.Exists(Path.Combine(markdownDirectory, "capture-relative.txt")).ShouldBeTrue();
            File.Exists(Path.Combine(markdownDirectory, "teardown-relative.txt")).ShouldBeTrue();
            File.Exists(
                Path.Combine(markdownDirectory, "recordings", "captured.json")
            ).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RecorderFailureDoesNotSkipLaterBatchJobs()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdownPath = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <!-- c2s:: -o missing.svg --replay missing.json -- echo unreachable -->
                <!-- c2s:: -o later.svg -- echo later -->
                """
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdownPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(1);
            File.Exists(Path.Combine(output, "missing.svg")).ShouldBeFalse();
            File.Exists(Path.Combine(output, "later.svg")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "console2svg-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
