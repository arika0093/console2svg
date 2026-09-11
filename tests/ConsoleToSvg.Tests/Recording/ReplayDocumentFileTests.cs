using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Recording;

public sealed class ReplayDocumentFileTests
{
    [Test]
    public async Task LegacyStringVersionMigratesToExactSleepKeyAndRawSteps()
    {
        var jsonPath = NewPath(".json");
        var yamlPath = ReplayDocumentFile.GetCanonicalPath(jsonPath);
        try
        {
            await File.WriteAllTextAsync(
                jsonPath,
                """
                {"version":"1","totalDuration":1,"replay":[
                  {"time":1.5,"key":"a","modifiers":[],"type":"keydown"},
                  {"tick":0.25,"key":"\u001b[200~","modifiers":[],"type":"raw"}
                ]}
                """
            );

            var document = await ReplayDocumentFile.ReadAsync(jsonPath, CancellationToken.None);

            document.Steps.Count.ShouldBe(4);
            document.Steps[0].Sleep.ShouldBe("1500ms");
            document.Steps[1].Key.ShouldBe("a");
            document.Steps[2].Sleep.ShouldBe("250ms");
            document.Steps[3].Raw.ShouldBe("\u001b[200~");
            document.Timeout.ShouldBe("2000ms");
            File.Exists(yamlPath).ShouldBeTrue();
        }
        finally
        {
            DeleteReplayFiles(jsonPath);
        }
    }

    [Test]
    public async Task LegacyMissingVersionIsMigratedAsV1()
    {
        var jsonPath = NewPath(".json");
        try
        {
            await File.WriteAllTextAsync(
                jsonPath,
                """{"replay":[{"time":0,"key":"Enter","modifiers":[],"type":"keydown"}]}"""
            );

            var document = await ReplayDocumentFile.ReadAsync(jsonPath, CancellationToken.None);

            document.Steps.Count.ShouldBe(2);
            document.Steps[0].Sleep.ShouldBe("0s");
            document.Steps[1].Key.ShouldBe("Enter");
        }
        finally
        {
            DeleteReplayFiles(jsonPath);
        }
    }

    [Test]
    public async Task YamlV2LoadAndSavePreservesMappedWaitAndSessionOptions()
    {
        var yamlPath = NewPath(".yaml");
        try
        {
            await File.WriteAllTextAsync(
                yamlPath,
                """
                $version: 2
                options:
                  terminal:
                    width: 100
                    height: 30
                  appearance:
                    theme: dark
                    font:
                      family: Cascadia Mono
                      size: 14
                  render:
                    mode: video
                    fps: 12
                    timing: deterministic
                    loop: true
                command: dotnet test
                defaults:
                  timeout: 10s
                steps:
                  - waitFor: "$ "
                  - input: "こんにちは"
                    interval: 1ms
                  - key: Enter
                  - waitFor:
                      regex: "Passed!"
                      newOutput: true
                      scope: scrollback
                      timeout: 2s
                """
            );

            var document = await ReplayDocumentFile.ReadAsync(yamlPath, CancellationToken.None);
            document.Options.Terminal.Width.ShouldBe(100);
            document.Options.Render.Mode.ShouldBe("video");
            document.Options.Appearance.Font.ShouldBe("Cascadia Mono");
            document.Options.Appearance.FontSize.ShouldBe(14);
            document.Steps[1].Input.ShouldBe("こんにちは");
            var waitFor = document.Steps[3].WaitFor
                ?? throw new InvalidOperationException("waitFor was not loaded.");
            waitFor.Regex.ShouldBe("Passed!");
            waitFor.NewOutput.ShouldBeTrue();
            document.Steps[3].Timeout.ShouldBe("2s");

            await ReplayDocumentFile.WriteAsync(yamlPath, document, CancellationToken.None);
            var saved = await File.ReadAllTextAsync(yamlPath, Encoding.UTF8);
            saved.ShouldContain("$version: 2");
            saved.ShouldContain("waitFor:");
            saved.ShouldContain("regex: Passed!");

            var roundTrip = await ReplayDocumentFile.ReadAsync(yamlPath, CancellationToken.None);
            roundTrip.Steps[3].WaitFor!.Scope.ShouldBe("scrollback");
        }
        finally
        {
            DeleteReplayFiles(yamlPath);
        }
    }

    private static string NewPath(string extension) =>
        Path.Combine(
            Environment.CurrentDirectory,
            $"console2svg-replay-test-{Guid.NewGuid():N}{extension}"
        );

    private static void DeleteReplayFiles(string suppliedPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(suppliedPath))!;
        var stem = Path.GetFileNameWithoutExtension(suppliedPath);
        foreach (var path in Directory.EnumerateFiles(directory, $"{stem}*"))
        {
            File.Delete(path);
        }
        var backupDirectory = Path.Combine(directory, ".backup");
        if (Directory.Exists(backupDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(backupDirectory, $"*{stem}*"))
            {
                File.Delete(path);
            }
        }
    }
}
