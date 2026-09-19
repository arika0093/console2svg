using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConsoleToSvg.Batch;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchMarkdownTests
{
    [Test]
    public void ImmediatelyPrecedingShellFenceIsImplicitCapture()
    {
        var result = Parse(
            """
            ```bash
            dotnet --info
            ```
            <!-- c2s:: -w 100 -o cmd-info.svg -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs.Count.ShouldBe(1);
        result.Jobs[0].Capture.ShouldBe("dotnet --info");
        result.Jobs[0].Width.ShouldBe(100);
        result.Jobs[0].OutputRelative.ShouldBe("cmd-info.svg");
    }

    [Test]
    public void NonAdjacentUnnamedFenceIsNotImplicitlySelected()
    {
        var result = Parse(
            """
            ```bash
            echo wrong
            ```

            Explanatory text.

            <!-- c2s:: -->
            """
        );

        result.Jobs.ShouldBeEmpty();
        result.Errors.Single().Message.ShouldContain("immediately after");
    }

    [Test]
    public void InlineCommandOverridesImplicitFence()
    {
        var result = Parse(
            """
            ```bash
            echo wrong
            ```
            <!-- c2s:: -h 10 -- echo right -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Capture.ShouldBe("echo right");
        result.Jobs[0].Height.ShouldBe(10);
    }

    [Test]
    public void YamlSectionsAreParsedByVYaml()
    {
        var result = Parse(
            """
            <!-- c2s:: -w 90 -d macos
            setup: |
              echo prepare
              echo ready
            capture: |
              echo first
              echo second
            teardown: echo clean
            -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Setup.ShouldBe("echo prepare\necho ready");
        result.Jobs[0].Capture.ShouldBe("echo first\necho second");
        result.Jobs[0].Teardown.ShouldBe("echo clean");
        result.Jobs[0].Window.ShouldBe("macos");
    }

    [Test]
    public void EmptyYamlCaptureUsesImmediatelyPrecedingFence()
    {
        var result = Parse(
            """
            ```sh
            echo captured
            ```
            <!-- c2s::
            setup: echo setup
            capture:
            teardown:
            -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Setup.ShouldBe("echo setup");
        result.Jobs[0].Capture.ShouldBe("echo captured");
        result.Jobs[0].Teardown.ShouldBeEmpty();
    }

    [Test]
    public void NamedCodeBlocksCanBeReferencedFromYaml()
    {
        var result = Parse(
            """
            ```csharp c2s-id=program
            Console.WriteLine("hi");
            ```

            ```bash c2s-id=run
            dotnet run test.cs > result.txt
            ```

            ```bash c2s-id=result
            cat result.txt
            ```

            <!-- c2s:: -o test.svg
            setup: |
              cat > test.cs <<'EOF'
              {code:program}
              EOF
              {code:run}
            capture: "{code:result}"
            teardown: rm -f test.cs result.txt
            -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Setup.ShouldContain("Console.WriteLine(\"hi\");");
        result.Jobs[0].Setup.ShouldContain("dotnet run test.cs > result.txt");
        result.Jobs[0].Capture.ShouldBe("cat result.txt");
    }

    [Test]
    public void DuplicateCodeBlockIdsAreRejected()
    {
        var result = Parse(
            """
            ```text c2s-id=sample
            first
            ```
            ```text c2s-id=sample
            second
            ```
            """
        );

        result.Errors.Single().Message.ShouldContain("duplicate c2s-id");
    }

    [Test]
    public void ForwardCodeBlockReferenceIsRejected()
    {
        var result = Parse(
            """
            <!-- c2s::
            capture: "{code:later}"
            -->
            ```bash c2s-id=later
            echo later
            ```
            """
        );

        result.Jobs.ShouldBeEmpty();
        result.Errors.Single().Message.ShouldContain("no preceding code block");
    }

    [Test]
    public void PositionalAndLanguagePlaceholdersAreRejected()
    {
        foreach (var placeholder in new[] { "{code}", "{code/bash}", "{code:1}" })
        {
            var result = Parse($"<!-- c2s:: -- echo {placeholder} -->");
            result.Jobs.ShouldBeEmpty();
            result.Errors.Single().Message.ShouldContain("use '{code:<c2s-id>}'");
        }
    }

    [Test]
    public void MarkersInsideCodeFencesAreIgnored()
    {
        var result = Parse(
            """
            ````markdown
            ```bash
            echo example
            ```
            <!-- c2s:: -- echo must-not-run -->
            ````
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs.ShouldBeEmpty();
    }

    [Test]
    public void TildeFenceCanBeImplicitCapture()
    {
        var result = Parse(
            """
            ~~~bash
            echo hi
            ~~~
            <!-- c2s:: -->
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Capture.ShouldBe("echo hi");
    }

    [Test]
    public void InlineAndYamlCaptureConflictIsRejected()
    {
        var result = Parse(
            """
            <!-- c2s:: -- echo inline
            capture: echo yaml
            -->
            """
        );

        result.Jobs.ShouldBeEmpty();
        result.Errors.Single().Message.ShouldContain("both");
    }

    [Test]
    public void UnknownYamlKeyIsRejected()
    {
        var result = Parse(
            """
            <!-- c2s::
            command: echo nope
            -->
            """
        );

        result.Jobs.ShouldBeEmpty();
        result.Errors.Single().Message.ShouldContain("unknown marker YAML key");
    }

    [Test]
    public void OutputTraversalAndMissingExtensionAreRejected()
    {
        foreach (
            var output in new[] { "../evil.svg", "/abs.svg", "sub/../../evil.svg", "no-extension" }
        )
        {
            var result = Parse($"<!-- c2s:: -o {output} -- echo hi -->");
            result.Jobs.ShouldBeEmpty();
            result.Errors.Count.ShouldBe(1);
        }
    }

    [Test]
    public void CaptureOptionsAndRasterOutputUseTheCaptureParser()
    {
        var result = Parse(
            "<!-- c2s:: -o image.gif -w 100 -v --fps 30 --sleep 0.5 --background '#003060' '#0060c0' --opacity 0.85 -- echo hi -->"
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].OutputRelative.ShouldBe("image.gif");
        result.Jobs[0].CaptureOptions.Background.ShouldBe(["#003060", "#0060c0"]);
        result.Jobs[0].CaptureOptions.Opacity.ShouldBe(0.85);
        result.Jobs[0].CaptureOptions.VideoFps.ShouldBe(30);
        result.Jobs[0].CaptureOptions.VideoSleep.ShouldBe(0.5);
    }

    [Test]
    public void AttachedOutFormIsTreatedAsExplicitOutput()
    {
        var result = Parse("<!-- c2s:: --out=image.gif -w 40 -- echo hi -->");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].OutputRelative.ShouldBe("image.gif");
    }

    [Test]
    public void ExistingAssociatedImageTargetIsRememberedForAutoOutput()
    {
        var result = Parse(
            """
            ```bash
            echo hi
            ```
            <!-- c2s:: -->

            ![old](../../assets/original/test-7.svg)
            """
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].OutputAuto.ShouldBeTrue();
        result.Jobs[0].ExistingLinkTarget.ShouldBe("../../assets/original/test-7.svg");
    }

    [Test]
    public void ExistingHtmlImageIsRememberedAndRewritten()
    {
        var markdown =
            "| <!-- c2s:: -o window/macos.svg -- echo hi --><img src=\"./assets/window/macos.svg\" width=\"400\"> |";
        var result = Parse(markdown);

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].ExistingLinkTarget.ShouldBe("./assets/window/macos.svg");
        var rewritten = BatchMarkdown.RewriteLinks(
            markdown,
            [new(result.Jobs[0], "assets/window/new-macos.svg")]
        );

        rewritten.ShouldContain("src=\"assets/window/new-macos.svg\" width=\"400\"");
        rewritten.ShouldNotContain("./assets/window/macos.svg");
    }

    [Test]
    public void MdxMarkerIsParsed()
    {
        var result = Parse(
            """
            ```bash
            echo hi
            ```
            {/* c2s:: -o mdx.svg */}
            """,
            "page.mdx"
        );

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Kind.ShouldBe(BatchMarkerKind.Mdx);
    }

    [Test]
    public void UnsupportedMarkerOptionIsRejected()
    {
        var result = Parse("<!-- c2s:: --bogus 1 -- echo hi -->");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Single().Message.ShouldContain("unsupported marker option");
    }

    [Test]
    public void CaptureSideEffectOptionsNotImplementedByBatchAreRejected()
    {
        foreach (
            var option in new[]
            {
                "--save-cast output.cast",
                "--save-frames frames",
                "--embed-cast",
                "--embed-logs",
                "--embed-replay",
                "--embed-debug",
                "--stdout",
            }
        )
        {
            var result = Parse($"<!-- c2s:: {option} -- echo hi -->");

            result.Jobs.ShouldBeEmpty();
            result.Errors.Single().Message.ShouldContain("not implemented by batch markdown");
        }
    }

    [Test]
    public void RewriteInsertsMissingLink()
    {
        var markdown = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o a.svg -->
            """;
        var result = Parse(markdown);

        var rewritten = BatchMarkdown.RewriteLinks(
            markdown,
            [new BatchLink(result.Jobs[0], "../assets/a.svg")]
        );

        rewritten.ShouldContain("![echo hi](../assets/a.svg)");
    }

    [Test]
    public void RewriteUpdatesExistingLinkAndIsIdempotent()
    {
        var markdown = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o a.svg -->
            ![old](./old.svg)
            """;
        var result = Parse(markdown);
        var link = new BatchLink(result.Jobs[0], "../assets/a.svg");

        var rewritten = BatchMarkdown.RewriteLinks(markdown, [link]);
        var reparsed = Parse(rewritten);
        var second = BatchMarkdown.RewriteLinks(
            rewritten,
            [new BatchLink(reparsed.Jobs[0], "../assets/a.svg")]
        );

        rewritten.ShouldContain("![old](../assets/a.svg)");
        rewritten.ShouldNotContain("old.svg");
        second.ShouldBe(rewritten);
    }

    [Test]
    public async Task BatchMarkdownCliMapsOnlyBatchControls()
    {
        var invocation = await InvokeAsync(
            "batch",
            "markdown",
            "-i",
            "docs/guide.md",
            "-o",
            "assets",
            "--filter",
            "reference/**",
            "tutorial/*.md",
            "--dry-run"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options.ShouldNotBeNull();
        invocation.Options!.Workflow.ShouldBe(Workflow.Batch);
        invocation.Options.BatchInputPath.ShouldBe("docs/guide.md");
        invocation.Options.BatchOutputDir.ShouldBe("assets");
        invocation.Options.BatchFilters.ShouldBe(["reference/**", "tutorial/*.md"]);
        invocation.Options.BatchDryRun.ShouldBeTrue();
    }

    [Test]
    public async Task BatchMarkdownCliMapsPlaceholder()
    {
        var invocation = await InvokeAsync("batch", "markdown", "--placeholder");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options.ShouldNotBeNull();
        invocation.Options!.RequestedBatchAction.ShouldBe(BatchAction.Markdown);
        invocation.Options.BatchPlaceholder.ShouldBeTrue();
    }

    [Test]
    public async Task BatchRestoreCliMapsRestoreControls()
    {
        var invocation = await InvokeAsync(
            "batch",
            "restore",
            "https://example.com/manifest.json",
            "-o",
            "assets",
            "--filter",
            "**/*.svg",
            "--force",
            "--prune",
            "--dry-run"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options.ShouldNotBeNull();
        invocation.Options!.RequestedBatchAction.ShouldBe(BatchAction.Restore);
        invocation.Options.BatchInputPath.ShouldBe("https://example.com/manifest.json");
        invocation.Options.BatchOutputDir.ShouldBe("assets");
        invocation.Options.BatchFilters.ShouldBe(["**/*.svg"]);
        invocation.Options.BatchForce.ShouldBeTrue();
        invocation.Options.BatchPrune.ShouldBeTrue();
        invocation.Options.BatchDryRun.ShouldBeTrue();
    }

    [Test]
    public async Task LegacyBatchRunCommandIsNotAccepted()
    {
        var invocation = await InvokeAsync("batch", "run");

        invocation.ExitCode.ShouldNotBe(0);
        invocation.Options.ShouldBeNull();
    }

    private static BatchParseResult Parse(string markdown, string path = "guide.md") =>
        BatchMarkdown.Parse(markdown, path);

    private static async Task<Invocation> InvokeAsync(params string[] args)
    {
        AppOptions? options = null;
        var commandLine = ConsoleToSvgCommandLine.Create(
            (parsed, _, _) =>
            {
                options = parsed;
                return Task.FromResult(0);
            }
        );
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await commandLine
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
        return new Invocation(exitCode, options, output.ToString(), error.ToString());
    }

    private sealed record Invocation(
        int ExitCode,
        AppOptions? Options,
        string Output,
        string Error
    );
}
