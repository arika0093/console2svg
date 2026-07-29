using System;
using System.IO;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class ConfigurationLoaderTests
{
    [Test]
    public void AppearanceConfigurationIsLoadedAndCommandLineTakesPrecedence()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "appearance.yml");
        try
        {
            File.WriteAllText(configPath, """
                $schema: https://raw.githubusercontent.com/arika0093/console2svg/main/docs/console2svg.schema.json
                outputPath: capture.svg
                width: "120"
                mode: video
                videoFps: "24"
                appearance:
                  window: macos-pc
                  padding: 16
                  background: ["#102030", "#405060"]
                  fontSize: 18
                """);

            var ok = OptionParser.TryParse(
                ["--config", configPath, "--window", "windows", "--padding", "4"],
                out var options,
                out var error,
                out _
            );

            ok.ShouldBeTrue(error);
            options!.Window.ShouldBe("windows");
            options.Padding.ShouldBe(4d);
            options.Background.ShouldBe(["#102030", "#405060"]);
            options.FontSize.ShouldBe(18d);
            options.OutputPath.ShouldBe("capture.svg");
            options.Width.ShouldBe(120);
            options.Mode.ShouldBe(OutputMode.Video);
            options.VideoFps.ShouldBe(24d);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void IncludedAppearanceConfigurationIsAppliedBeforeCurrentFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "base.yml"), "appearance:\n  window: macos\n  padding: 12\n  background: ['#001122']\n  pcMode: true\n");
            var configPath = Path.Combine(directory, "console2svg.yml");
            File.WriteAllText(configPath, "include: [base.yml]\nappearance:\n  padding: 6\n  background: ['#334455']\n  pcMode: false\n  theme: light\n");

            var ok = OptionParser.TryParse(["--config", configPath], out var options, out var error, out _);

            ok.ShouldBeTrue(error);
            options!.Window.ShouldBe("macos");
            options.Padding.ShouldBe(6d);
            options.Background.ShouldBe(["#334455"]);
            options.PcMode.ShouldBeFalse();
            options.Theme.ShouldBe("light");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
