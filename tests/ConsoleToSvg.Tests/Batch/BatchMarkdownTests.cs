using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using ConsoleToSvg.Batch;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchMarkdownTests
{
    [Test]
    public void FenceFallbackUsesPrecedingBashBlock()
    {
        var md = """
            ```bash
            dotnet --info
            ```
            <!-- c2s:: -w 100 -o cmd-info.svg -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs.Count.ShouldBe(1);
        result.Jobs[0].Capture.ShouldBe("dotnet --info");
        result.Jobs[0].Setup.ShouldBe(string.Empty);
        result.Jobs[0].Width.ShouldBe(100);
        result.Jobs[0].OutputRelative.ShouldBe("cmd-info.svg");
        result.Jobs[0].OutputAuto.ShouldBeFalse();
    }

    [Test]
    public void ExplicitCommandOverridesFence()
    {
        var md = """
            ```bash
            dotnet --info
            ```
            <!-- c2s:: -w 100 -o cmd-crop.svg -- dotnet --list-sdks -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Capture.ShouldBe("dotnet --list-sdks");
    }

    [Test]
    public void Console2SvgAliasAndAutoNaming()
    {
        var md = """
            ```sh
            echo hi
            ```
            <!-- console2svg:: -h 10 -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Capture.ShouldBe("echo hi");
        result.Jobs[0].Height.ShouldBe(10);
        result.Jobs[0].OutputRelative.ShouldBe("guide-1.svg");
        result.Jobs[0].OutputAuto.ShouldBeTrue();
    }

    [Test]
    public void SetupCaptureTeardownSplit()
    {
        var md = """
            <!-- c2s:: -o server.svg --
            echo starting
            ---
            echo hello
            ---
            echo done
            -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Setup.ShouldBe("echo starting");
        result.Jobs[0].Capture.ShouldBe("echo hello");
        result.Jobs[0].Teardown.ShouldBe("echo done");
    }

    [Test]
    public void MultilineWithoutSeparatorIsError()
    {
        var md = """
            <!-- c2s:: -o x.svg --
            echo a
            echo b
            -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].Message.ShouldContain("---");
    }

    [Test]
    public void ThreeSeparatorsIsError()
    {
        var md = """
            <!-- c2s:: -o x.svg --
            a
            ---
            b
            ---
            c
            ---
            d
            -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Count.ShouldBe(1);
    }

    [Test]
    public void MissingCommandIsError()
    {
        var md = """
            ```csharp
            var x = 1;
            ```
            <!-- c2s:: -o x.svg -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].Message.ShouldContain("no command found");
    }

    [Test]
    public void OutputTraversalIsRejected()
    {
        foreach (var bad in new[] { "../evil.svg", "/abs.svg", "sub/../../evil.svg", "x.png" })
        {
            var md = $"""
                ```bash
                echo hi
                ```
                <!-- c2s:: -o {bad} -->
                """;
            var result = BatchMarkdown.Parse(md, "guide.md");
            result.Jobs.ShouldBeEmpty();
            result.Errors.Count.ShouldBe(1);
        }
    }

    [Test]
    public void SubdirectoryOutputIsAllowed()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o ./sub/cmd.svg -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].OutputRelative.ShouldBe("sub/cmd.svg");
    }

    [Test]
    public void CodePlaceholderExpandsRaw()
    {
        var md = """
            ```csharp
            Console.WriteLine("hi");
            ```
            ```bash
            dotnet run test.cs
            ```
            <!-- c2s:: -o test.svg --
            cat <<'EOF' > test.cs
            {code/csharp}
            EOF
            ---
            dotnet run test.cs
            -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Errors.ShouldBeEmpty();
        result.Jobs[0].Setup.ShouldContain("Console.WriteLine(\"hi\");");
        result.Jobs[0].Capture.ShouldBe("dotnet run test.cs");
    }

    [Test]
    public void MissingPlaceholderTargetIsError()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o x.svg -- echo {code/python} -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Count.ShouldBe(1);
    }

    [Test]
    public void MdxMarkerIsParsed()
    {
        var md = """
            ```bash
            echo hi
            ```
            {/* c2s:: -o mdx.svg */}
            """;
        var result = BatchMarkdown.Parse(md, "page.mdx");

        result.Errors.ShouldBeEmpty();
        result.Jobs.Count.ShouldBe(1);
        result.Jobs[0].Kind.ShouldBe(BatchMarkerKind.Mdx);
        result.Jobs[0].Capture.ShouldBe("echo hi");
    }

    [Test]
    public void UnsupportedOptionIsError()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: --bogus 1 -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");

        result.Jobs.ShouldBeEmpty();
        result.Errors.Count.ShouldBe(1);
    }

    [Test]
    public void RewriteInsertsMissingLink()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o a.svg -->
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");
        var rewritten = BatchMarkdown.RewriteLinks(
            md,
            [new BatchLink(result.Jobs[0], "../assets/a.svg")]
        );

        rewritten.ShouldContain("<!-- c2s:: -o a.svg -->\n![echo hi](../assets/a.svg)");
    }

    [Test]
    public void RewriteUpdatesExistingLink()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o a.svg -->
            ![old](./old.svg)
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");
        var rewritten = BatchMarkdown.RewriteLinks(
            md,
            [new BatchLink(result.Jobs[0], "../assets/a.svg")]
        );

        rewritten.ShouldContain("![echo hi](../assets/a.svg)");
        rewritten.ShouldNotContain("old.svg");
    }

    [Test]
    public void RewriteIsIdempotent()
    {
        var md = """
            ```bash
            echo hi
            ```
            <!-- c2s:: -o a.svg -->
            ![echo hi](../assets/a.svg)
            """;
        var result = BatchMarkdown.Parse(md, "guide.md");
        var rewritten = BatchMarkdown.RewriteLinks(
            md,
            [new BatchLink(result.Jobs[0], "../assets/a.svg")]
        );

        rewritten.ShouldBe(md);
    }

    [Test]
    public async Task BatchRunCliMapping()
    {
        var invocation = await InvokeAsync(
            "batch",
            "run",
            "-i",
            "docs",
            "-o",
            "assets",
            "--dry",
            "--cached",
            "-w",
            "80"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options.ShouldNotBeNull();
        invocation.Options!.Workflow.ShouldBe(Workflow.Batch);
        invocation.Options.BatchDry.ShouldBeTrue();
        invocation.Options.BatchCached.ShouldBeTrue();
        invocation.Options.Width.ShouldBe(80);
    }

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
