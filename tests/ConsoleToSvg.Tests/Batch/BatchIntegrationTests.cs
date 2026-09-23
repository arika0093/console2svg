using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ConsoleToSvg.Batch;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Batch;

public sealed class BatchIntegrationTests
{
    [Test]
    public async Task EquivalentLocalizedJobsShareRecipeAddressedObject()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var output = Path.Combine(root, "assets");
            var manifest = Path.Combine(output, "assets.json");
            Directory.CreateDirectory(Path.Combine(docs, "en"));
            Directory.CreateDirectory(Path.Combine(docs, "ja"));
            const string marker = "<!-- c2s:: -- echo localized -->";
            await File.WriteAllTextAsync(Path.Combine(docs, "en", "guide.md"), marker);
            await File.WriteAllTextAsync(Path.Combine(docs, "ja", "guide.md"), marker);

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(0);
            var generated = Directory.GetFiles(Path.Combine(output, "generated"));
            generated.Length.ShouldBe(1);
            var canonical = generated.Single();
            (await File.ReadAllTextAsync(canonical)).ShouldNotBeEmpty();
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
            var assets = document.RootElement.GetProperty("assets");
            assets.EnumerateObject().Count().ShouldBe(2);
            document.RootElement.GetProperty("objects").EnumerateObject().Count().ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PlaceholderCreatesCanonicalObjectWithoutExecutingCommandsOrManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdown = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            var sideEffect = Path.Combine(root, "executed.txt");
            var manifest = Path.Combine(output, "assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(markdown)!);
            await File.WriteAllTextAsync(
                markdown,
                $"<!-- c2s:: -- echo executed > \"{sideEffect.Replace('\\', '/')}\" -->"
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdown,
                "-o",
                output,
                "--placeholder",
            ]);

            exitCode.ShouldBe(0);
            File.Exists(sideEffect).ShouldBeFalse();
            File.Exists(manifest).ShouldBeFalse();
            var canonical = Directory.GetFiles(Path.Combine(output, "generated")).Single();
            (await File.ReadAllTextAsync(canonical)).ShouldBe(string.Empty);

            exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                markdown,
                "-o",
                output,
                "--placeholder",
            ]);
            exitCode.ShouldBe(0);
            (await File.ReadAllTextAsync(canonical)).ShouldBe(string.Empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FormatOptionSelectsTheAutoOutputExtension()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(
                markdownPath,
                "<!-- c2s:: --format png -w 20 -h 2 -- echo formatted -->"
            );

            var args = new[]
            {
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--link-base",
                "/assets",
                "--placeholder",
            };
            (await Program.Main(args)).ShouldBe(0);

            Directory.GetFiles(Path.Combine(output, "generated"))
                .Single()
                .ShouldEndWith(".png");
            var rewritten = await File.ReadAllTextAsync(markdownPath);
            rewritten.ShouldMatch(@"!\[echo formatted\]\(/assets/generated/[0-9a-f]{12}\.png\)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FormatOptionRetargetsAnExistingHtmlSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "video.mdx");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <video controls width="600">
                  {/* c2s:: --format mp4 -w 20 -h 2 -v -- echo video */}
                  <source src="../assets/generated/old.svg" type="video/mp4" />
                </video>
                """
            );

            var args = new[]
            {
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--link-base",
                "/assets",
                "--placeholder",
            };
            (await Program.Main(args)).ShouldBe(0);

            Directory.GetFiles(Path.Combine(output, "generated")).Single().ShouldEndWith(".mp4");
            var rewritten = await File.ReadAllTextAsync(markdownPath);
            rewritten.ShouldMatch(
                @"<source src=""/assets/generated/[0-9a-f]{12}\.mp4"" type=""video/mp4"" />"
            );
            rewritten.ShouldNotContain("old.svg");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task BatchFormatOptionIsTheDefaultForMarkersWithoutTheirOwnFormat()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(
                markdownPath,
                """
                <!-- c2s:: -w 20 -h 2 -- echo defaulted -->
                <!-- c2s:: --format png -w 20 -h 2 -- echo explicit -->
                """
            );

            var args = new[]
            {
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--link-base",
                "/assets",
                "--format",
                "webp",
                "--placeholder",
            };
            (await Program.Main(args)).ShouldBe(0);

            var generated = Directory
                .GetFiles(Path.Combine(output, "generated"))
                .Select(Path.GetFileName)
                .ToArray();
            generated.Length.ShouldBe(2);
            generated.Count(name => name!.EndsWith(".webp")).ShouldBe(1);
            generated.Count(name => name!.EndsWith(".png")).ShouldBe(1);

            var rewritten = await File.ReadAllTextAsync(markdownPath);
            rewritten.ShouldMatch(@"!\[echo defaulted\]\(/assets/generated/[0-9a-f]{12}\.webp\)");
            rewritten.ShouldMatch(@"!\[echo explicit\]\(/assets/generated/[0-9a-f]{12}\.png\)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreVerifiesContentAndMaterializesAliases()
    {
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "restored");
            Directory.CreateDirectory(Path.Combine(published, "generated"));
            var asset = Path.Combine(published, "generated", "recipe.svg");
            var bytes = Encoding.UTF8.GetBytes("<svg>restored</svg>");
            await File.WriteAllBytesAsync(asset, bytes);
            var manifest = new BatchAssetManifest
            {
                Generator = new BatchAssetGenerator { Name = "test", Version = "1" },
                Objects =
                {
                    ["recipe.svg"] = new BatchAssetObject
                    {
                        Path = "generated/recipe.svg",
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                        Size = bytes.Length,
                        MediaType = "image/svg+xml",
                    },
                },
                Assets =
                {
                    ["en/guide.svg"] = new BatchLogicalAsset { Object = "recipe.svg" },
                    ["ja/guide.svg"] = new BatchLogicalAsset { Object = "recipe.svg" },
                },
            };
            var manifestPath = Path.Combine(published, "assets.json");
            await BatchAssets.WriteManifestAsync(manifest, manifestPath, default);

            var exitCode = await Program.Main([
                "batch",
                "restore",
                manifestPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(0);
            (await File.ReadAllTextAsync(Path.Combine(output, "en", "guide.svg")))
                .ShouldBe("<svg>restored</svg>");
            (await File.ReadAllTextAsync(Path.Combine(output, "ja", "guide.svg")))
                .ShouldBe("<svg>restored</svg>");

            await File.WriteAllTextAsync(Path.Combine(output, "generated", "recipe.svg"), "bad");
            exitCode = await Program.Main([
                "batch",
                "restore",
                manifestPath,
                "-o",
                output,
            ]);
            exitCode.ShouldBe(0);
            (await File.ReadAllTextAsync(Path.Combine(output, "generated", "recipe.svg")))
                .ShouldBe("<svg>restored</svg>");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreRejectsDestinationTraversal()
    {
        var root = CreateTempDirectory();
        try
        {
            var manifest = new BatchAssetManifest
            {
                Generator = new BatchAssetGenerator { Name = "test", Version = "1" },
                Objects =
                {
                    ["escaped"] = new BatchAssetObject
                    {
                        Path = "../escaped.svg",
                        Sha256 = new string('0', 64),
                        Size = 0,
                    },
                },
                Assets = { ["escaped.svg"] = new BatchLogicalAsset { Object = "escaped" } },
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await BatchAssets.WriteManifestAsync(manifest, manifestPath, default);

            var exitCode = await Program.Main([
                "batch",
                "restore",
                manifestPath,
                "-o",
                Path.Combine(root, "output"),
            ]);

            exitCode.ShouldBe(1);
            File.Exists(Path.Combine(root, "escaped.svg")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreIntegrityFailurePreservesExistingAsset()
    {
        var root = CreateTempDirectory();
        try
        {
            var publishedAsset = Path.Combine(root, "published.svg");
            var output = Path.Combine(root, "output");
            var destination = Path.Combine(output, "published.svg");
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(publishedAsset, "untrusted");
            await File.WriteAllTextAsync(destination, "existing");
            var manifest = new BatchAssetManifest
            {
                Generator = new BatchAssetGenerator { Name = "test", Version = "1" },
                Objects =
                {
                    ["asset"] = new BatchAssetObject
                    {
                        Path = "published.svg",
                        Sha256 = new string('0', 64),
                        Size = new FileInfo(publishedAsset).Length,
                    },
                },
                Assets =
                {
                    ["published.svg"] = new BatchLogicalAsset { Object = "asset" },
                },
            };
            var manifestPath = Path.Combine(root, "manifest.json");
            await BatchAssets.WriteManifestAsync(manifest, manifestPath, default);

            var exitCode = await Program.Main([
                "batch",
                "restore",
                manifestPath,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(1);
            (await File.ReadAllTextAsync(destination)).ShouldBe("existing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FilteredGenerationMergesAssetsAndRemovesSelectedStaleEntries()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var output = Path.Combine(root, "assets");
            var first = Path.Combine(docs, "first.md");
            var second = Path.Combine(docs, "second.md");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(first, "<!-- c2s:: -- echo first -->");
            await File.WriteAllTextAsync(second, "<!-- c2s:: -- echo second -->");
            (await Program.Main(["batch", "markdown", "-i", docs, "-o", output]))
                .ShouldBe(0);
            var manifestPath = Path.Combine(output, "assets.json");
            var original = await BatchAssets.ReadManifestAsync(manifestPath, default);
            original.Assets.Count.ShouldBe(2);
            var secondObject = original.Assets["second-1.svg"].Object;

            await File.WriteAllTextAsync(first, "This document no longer owns an asset.");
            (await Program.Main([
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--filter",
                "first.md",
            ])).ShouldBe(0);

            var updated = await BatchAssets.ReadManifestAsync(manifestPath, default);
            updated.Assets.Keys.ShouldBe(["second-1.svg"]);
            updated.Assets["second-1.svg"].Object.ShouldBe(secondObject);
            updated.Objects.Keys.ShouldBe([secondObject]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FailedGenerationLeavesPreviousManifestUntouched()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdown = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdown)!);
            await File.WriteAllTextAsync(markdown, "<!-- c2s:: -- echo valid -->");
            (await Program.Main(["batch", "markdown", "-i", markdown, "-o", output]))
                .ShouldBe(0);
            var manifestPath = Path.Combine(output, "assets.json");
            var previous = await File.ReadAllBytesAsync(manifestPath);

            await File.WriteAllTextAsync(
                markdown,
                """
                <!-- c2s::
                setup: console2svg-command-that-does-not-exist
                capture: echo unreachable
                -->
                """
            );
            (await Program.Main(["batch", "markdown", "-i", markdown, "-o", output]))
                .ShouldBe(1);

            (await File.ReadAllBytesAsync(manifestPath)).ShouldBe(previous);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MarkdownRejectsReservedAssetPathsBeforeExecution()
    {
        var root = CreateTempDirectory();
        try
        {
            var markdown = Path.Combine(root, "docs", "guide.md");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.GetDirectoryName(markdown)!);
            await File.WriteAllTextAsync(
                markdown,
                "<!-- c2s:: -o assets.json -- echo must-not-run -->"
            );

            (await Program.Main(["batch", "markdown", "-i", markdown, "-o", output]))
                .ShouldBe(1);

            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreAcceptsDirectorySourceAndPrunesOnlyPreviouslyManagedFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            Directory.CreateDirectory(output);
            var oldBytes = Encoding.UTF8.GetBytes("old");
            await File.WriteAllBytesAsync(Path.Combine(published, "old.svg"), oldBytes);
            var firstManifest = CreateManifest("old", "old.svg", "logical-old.svg", oldBytes);
            await BatchAssets.WriteManifestAsync(
                firstManifest,
                Path.Combine(published, "assets.json"),
                default
            );
            (await Program.Main(["batch", "restore", published, "-o", output])).ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(output, "unrelated.txt"), "keep");

            var newBytes = Encoding.UTF8.GetBytes("new");
            await File.WriteAllBytesAsync(Path.Combine(published, "new.svg"), newBytes);
            var secondManifest = CreateManifest("new", "new.svg", "logical-new.svg", newBytes);
            await BatchAssets.WriteManifestAsync(
                secondManifest,
                Path.Combine(published, "assets.json"),
                default
            );
            (await Program.Main([
                "batch",
                "restore",
                published,
                "-o",
                output,
                "--prune",
            ])).ShouldBe(0);

            File.Exists(Path.Combine(output, "old.svg")).ShouldBeFalse();
            File.Exists(Path.Combine(output, "logical-old.svg")).ShouldBeFalse();
            File.Exists(Path.Combine(output, "new.svg")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "logical-new.svg")).ShouldBeTrue();
            File.Exists(Path.Combine(output, "unrelated.txt")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreDryRunFiltersLogicalAssetsWithoutWritingFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            var first = Encoding.UTF8.GetBytes("first");
            var second = Encoding.UTF8.GetBytes("second");
            await File.WriteAllBytesAsync(Path.Combine(published, "first.svg"), first);
            await File.WriteAllBytesAsync(Path.Combine(published, "second.svg"), second);
            var manifest = CreateManifest("first", "first.svg", "en/first.svg", first);
            var secondManifest = CreateManifest("second", "second.svg", "ja/second.svg", second);
            manifest.Objects.Add("second", secondManifest.Objects["second"]);
            manifest.Assets.Add("ja/second.svg", secondManifest.Assets["ja/second.svg"]);
            var manifestPath = Path.Combine(published, "assets.json");
            await BatchAssets.WriteManifestAsync(manifest, manifestPath, default);

            var result = await BatchAssets.RestoreAsync(
                manifestPath,
                output,
                ["en/**"],
                force: false,
                prune: false,
                dryRun: true,
                cancellationToken: default
            );

            result.Restored.ShouldBe(1);
            result.Filtered.ShouldBe(1);
            result.Materialized.ShouldBe(1);
            result.Actions.ShouldContain("restore first.svg");
            result.Actions.ShouldContain("materialize en/first.svg -> first.svg");
            Directory.Exists(output).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreAcceptsGitSourceWithRefAndSubdirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var repository = Path.Combine(root, "repository");
            var published = Path.Combine(repository, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            var content = Encoding.UTF8.GetBytes("from git");
            await File.WriteAllBytesAsync(Path.Combine(published, "object.svg"), content);
            await BatchAssets.WriteManifestAsync(
                CreateManifest("object", "object.svg", "guide.svg", content),
                Path.Combine(published, "assets.json"),
                default
            );
            RunGit(repository, "init", "--quiet");
            RunGit(repository, "add", ".");
            RunGit(
                repository,
                "-c",
                "user.name=console2svg-tests",
                "-c",
                "user.email=tests@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "assets"
            );
            var source = new Uri(repository + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/')
                + "#HEAD:published";

            (await Program.Main(["batch", "restore", source, "-o", output])).ShouldBe(0);

            (await File.ReadAllTextAsync(Path.Combine(output, "guide.svg"))).ShouldBe("from git");
        }
        finally
        {
            RepositorySourceAcquirer.DeleteCheckout(root);
        }
    }

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
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
            var rewritten = await File.ReadAllTextAsync(markdownPath);
            rewritten.ShouldMatch(@"!\[echo hello\]\(../../assets/generated/[0-9a-f]{12}\.svg\)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ObjectLinksUsePublicBaseAndRemainIdempotent()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var markdownPath = Path.Combine(docs, "guide.md");
            var output = Path.Combine(root, "public", "assets");
            Directory.CreateDirectory(docs);
            await File.WriteAllTextAsync(
                markdownPath,
                "<!-- c2s:: -w 20 -h 2 -- echo public -->"
            );

            var args = new[]
            {
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
                "--link-base",
                "/assets",
            };
            (await Program.Main(args)).ShouldBe(0);

            var first = await File.ReadAllTextAsync(markdownPath);
            first.ShouldMatch(
                @"!\[echo public\]\(/assets/generated/[0-9a-f]{12}\.svg\)"
            );
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
            File.Exists(Path.Combine(output, "guide-1.svg")).ShouldBeFalse();

            (await Program.Main(args)).ShouldBe(0);
            (await File.ReadAllTextAsync(markdownPath)).ShouldBe(first);
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
            File.Exists(Path.Combine(output, "guide-2.svg")).ShouldBeFalse();
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
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
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
    public async Task IdenticalOutputProducersAcrossMarkdownFilesShareOneGeneration()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.Combine(docs, "en"));
            Directory.CreateDirectory(Path.Combine(docs, "ja"));
            const string marker = "<!-- c2s:: -o shared.svg -- echo shared -->";
            await File.WriteAllTextAsync(Path.Combine(docs, "en", "guide.md"), marker);
            await File.WriteAllTextAsync(Path.Combine(docs, "ja", "guide.md"), marker);

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                docs,
                "-o",
                output,
            ]);

            exitCode.ShouldBe(0);
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
            var canonicalLink = "generated/";
            (await File.ReadAllTextAsync(Path.Combine(docs, "en", "guide.md")))
                .ShouldContain(canonicalLink);
            (await File.ReadAllTextAsync(Path.Combine(docs, "ja", "guide.md")))
                .ShouldContain(canonicalLink);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DifferentOutputProducersAcrossMarkdownFilesAreRejected()
    {
        var root = CreateTempDirectory();
        try
        {
            var docs = Path.Combine(root, "docs");
            var output = Path.Combine(root, "assets");
            Directory.CreateDirectory(Path.Combine(docs, "en"));
            Directory.CreateDirectory(Path.Combine(docs, "ja"));
            await File.WriteAllTextAsync(
                Path.Combine(docs, "en", "guide.md"),
                "<!-- c2s:: -o shared.svg -- echo English -->"
            );
            await File.WriteAllTextAsync(
                Path.Combine(docs, "ja", "guide.md"),
                "<!-- c2s:: -o shared.svg -- echo Japanese -->"
            );

            var exitCode = await Program.Main([
                "batch",
                "markdown",
                "-i",
                docs,
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
            var svg = await File.ReadAllTextAsync(
                Directory.GetFiles(Path.Combine(output, "generated")).Single()
            );
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

                <!-- c2s:: -o cwd.svg
                setup: echo setup > setup-relative.txt
                capture: echo capture > capture-relative.txt && echo capture
                teardown: echo teardown > teardown-relative.txt
                -->

                <!-- c2s:: -o replay-save.svg --replay-save recordings/captured.json -- echo replay-save -->
                """
            );

            var exitCode = await Program.Main(["batch", "markdown", "-i", docs, "-o", output]);

            exitCode.ShouldBe(0);
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(3);
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
            Directory.GetFiles(Path.Combine(output, "generated")).Length.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FilteredRestoreMergesManifestAndPrunesSelectedScopeOnly()
    {
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            Directory.CreateDirectory(output);
            var firstBytes = Encoding.UTF8.GetBytes("first");
            var secondBytes = Encoding.UTF8.GetBytes("second");
            await File.WriteAllBytesAsync(Path.Combine(published, "first.svg"), firstBytes);
            await File.WriteAllBytesAsync(Path.Combine(published, "second.svg"), secondBytes);
            var first = CreateManifest("first", "first.svg", "en/first.svg", firstBytes);
            var second = CreateManifest("second", "second.svg", "ja/second.svg", secondBytes);
            first.Objects.Add("second", second.Objects["second"]);
            first.Assets.Add("ja/second.svg", second.Assets["ja/second.svg"]);
            await BatchAssets.WriteManifestAsync(first, Path.Combine(published, "assets.json"), default);
            (await Program.Main(["batch", "restore", published, "-o", output])).ShouldBe(0);

            var thirdBytes = Encoding.UTF8.GetBytes("third");
            await File.WriteAllBytesAsync(Path.Combine(published, "third.svg"), thirdBytes);
            await BatchAssets.WriteManifestAsync(
                CreateManifest("third", "third.svg", "en/third.svg", thirdBytes),
                Path.Combine(published, "assets.json"),
                default
            );
            (await Program.Main([
                "batch",
                "restore",
                published,
                "-o",
                output,
                "--filter",
                "en/**",
                "--prune",
            ])).ShouldBe(0);

            File.Exists(Path.Combine(output, "first.svg")).ShouldBeFalse();
            File.Exists(Path.Combine(output, "en", "first.svg")).ShouldBeFalse();
            (await File.ReadAllTextAsync(Path.Combine(output, "en", "third.svg"))).ShouldBe("third");
            (await File.ReadAllTextAsync(Path.Combine(output, "ja", "second.svg"))).ShouldBe("second");
            var merged = await BatchAssets.ReadManifestAsync(Path.Combine(output, "assets.json"), default);
            merged.Assets.Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(["en/third.svg", "ja/second.svg"]);
            merged.Objects.ContainsKey("first").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestorePruneHandlesCasingOnlyRenameOnCaseInsensitiveFileSystems()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            Directory.CreateDirectory(Path.Combine(output, "Images"));
            var content = Encoding.UTF8.GetBytes("cased");
            await File.WriteAllBytesAsync(Path.Combine(published, "a.svg"), content);
            await BatchAssets.WriteManifestAsync(
                CreateManifest("asset", "images/a.svg", "images/a.svg", content),
                Path.Combine(published, "assets.json"),
                default
            );
            await File.WriteAllBytesAsync(Path.Combine(output, "Images", "A.svg"), content);
            await BatchAssets.WriteManifestAsync(
                CreateManifest("asset", "Images/A.svg", "Images/A.svg", content),
                Path.Combine(output, "assets.json"),
                default
            );
            (await Program.Main(["batch", "restore", published, "-o", output, "--prune"])).ShouldBe(0);
            (await File.ReadAllTextAsync(Path.Combine(output, "images", "a.svg"))).ShouldBe("cased");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreRejectsSymlinkedRepositorySubdirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var root = CreateTempDirectory();
        try
        {
            var repository = Path.Combine(root, "repository");
            var real = Path.Combine(repository, "real");
            Directory.CreateDirectory(real);
            var content = Encoding.UTF8.GetBytes("linked");
            await File.WriteAllBytesAsync(Path.Combine(real, "object.svg"), content);
            await BatchAssets.WriteManifestAsync(
                CreateManifest("object", "object.svg", "guide.svg", content),
                Path.Combine(real, "assets.json"),
                default
            );
            File.CreateSymbolicLink(Path.Combine(repository, "linked"), "real");
            RunGit(repository, "init", "--quiet");
            RunGit(repository, "add", ".");
            RunGit(
                repository,
                "-c",
                "user.name=console2svg-tests",
                "-c",
                "user.email=tests@example.invalid",
                "commit",
                "--quiet",
                "-m",
                "assets"
            );
            var source = new Uri(repository + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/')
                + "#HEAD:linked";
            var output = Path.Combine(root, "output");

            (await Program.Main(["batch", "restore", source, "-o", output])).ShouldBe(1);

            File.Exists(Path.Combine(output, "guide.svg")).ShouldBeFalse();
        }
        finally
        {
            RepositorySourceAcquirer.DeleteCheckout(root);
        }
    }

    [Test]
    public async Task RestoreRejectsOversizedContentDuringCopy()
    {
        var root = CreateTempDirectory();
        try
        {
            var published = Path.Combine(root, "published");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(published);
            var content = Encoding.UTF8.GetBytes("0123456789");
            await File.WriteAllBytesAsync(Path.Combine(published, "large.svg"), content);
            var manifest = CreateManifest("large", "large.svg", "large.svg", content);
            manifest.Objects["large"].Size = content.Length - 1;
            var manifestPath = Path.Combine(published, "assets.json");
            await BatchAssets.WriteManifestAsync(manifest, manifestPath, default);

            var result = await BatchAssets.RestoreAsync(
                manifestPath,
                output,
                [],
                force: false,
                prune: false,
                dryRun: false,
                cancellationToken: default
            );

            result.Failures.Count.ShouldBe(1);
            result.Failures.Single().ShouldContain("size mismatch");
            File.Exists(Path.Combine(output, "large.svg")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void RepositorySourceUrlClassification()
    {
        Program.IsRepositorySourceUrl("https://github.com/owner/repo").ShouldBeTrue();
        Program.IsRepositorySourceUrl("https://github.com/owner/repo.git").ShouldBeTrue();
        Program.IsRepositorySourceUrl("https://github.com/owner/repo#main:docs/assets").ShouldBeTrue();
        Program.IsRepositorySourceUrl("https://example.com/project/assets/assets.json").ShouldBeFalse();
        Program.IsRepositorySourceUrl("https://raw.githubusercontent.com/owner/repo/main/assets.json").ShouldBeFalse();
        Program.IsRepositorySourceUrl("https://github.com/owner/repo/blob/main/assets.json").ShouldBeFalse();
        Program.IsRepositorySourceUrl("owner/repo@main/docs/assets").ShouldBeFalse();
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

    private static BatchAssetManifest CreateManifest(
        string objectId,
        string objectPath,
        string logicalPath,
        byte[] content
    ) =>
        new()
        {
            Generator = new BatchAssetGenerator { Name = "test", Version = "1" },
            Objects =
            {
                [objectId] = new BatchAssetObject
                {
                    Path = objectPath,
                    Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                    Size = content.Length,
                    MediaType = "image/svg+xml",
                },
            },
            Assets = { [logicalPath] = new BatchLogicalAsset { Object = objectId } },
        };

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
#pragma warning disable S4036
        var startInfo = new ProcessStartInfo("git")
#pragma warning restore S4036
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, process.StandardError.ReadToEnd());
    }
}
