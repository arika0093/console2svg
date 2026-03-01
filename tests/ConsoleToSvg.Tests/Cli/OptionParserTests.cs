using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class OptionParserTests
{
    [Test]
    public void ShortFlagWidthParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-w", "120" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Width.ShouldBe(120);
    }

    [Test]
    public void ShortFlagHeightParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-h", "30" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Height.ShouldBe(30);
    }

    [Test]
    public void ShortFlagModeParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-m", "video" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Video);
    }

    [Test]
    public void PositionalArgumentTreatedAsCommand()
    {
        var ok = OptionParser.TryParse(
            new[] { "git log --oneline" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Command.ShouldBe("git log --oneline");
    }

    [Test]
    public void FontOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--font", "Consolas, monospace" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Font.ShouldBe("Consolas, monospace");
    }

    [Test]
    public void HelpFlagShowsHelp()
    {
        var ok = OptionParser.TryParse(new[] { "--help" }, out _, out _, out var showHelp);
        ok.ShouldBeTrue();
        showHelp.ShouldBeTrue();
    }

    [Test]
    public void VerboseShortFlagSetsVideoMode()
    {
        var ok = OptionParser.TryParse(new[] { "-v" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Video);
    }

    [Test]
    public void VerboseLongFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--verbose" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
    }

    [Test]
    public void VerboseFlagSetsVideoMode()
    {
        var ok = OptionParser.TryParse(new[] { "-v" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Video);
    }

    [Test]
    public void NullWidthHeightWhenNotSpecified()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Width.ShouldBeNull();
        options!.Height.ShouldBeNull();
    }

    [Test]
    public void MultiplePositionalArgsReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "cmd1", "cmd2" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Test]
    public void DoubleDashCollectsRemainingTokensAsCommand()
    {
        var ok = OptionParser.TryParse(
            new[] { "-w", "80", "--", "dotnet", "test.cs" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Width.ShouldBe(80);
        options.Command.ShouldBe("dotnet test.cs");
    }

    [Test]
    public void DoubleDashWithoutCommandReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("Expected command after --.");
    }

    [Test]
    public void VersionFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--version" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.ShowVersion.ShouldBeTrue();
    }

    [Test]
    public void WindowAndPaddingParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--window", "macos", "--padding", "4.5" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("macos");
        options.Padding.ShouldBe(4.5d);
    }

    [Test]
    public void InvalidWindowReturnsError()
    {
        // Unknown window values are now accepted at parse time and validated at load time.
        // The value is stored as-is; ChromeLoader.Load() will throw for unrecognised names/missing files.
        var ok = OptionParser.TryParse(
            new[] { "--window=linux" },
            out var options,
            out var error,
            out _
        );
        ok.ShouldBeTrue();
        error.ShouldBeNull();
        options!.Window.ShouldBe("linux");
    }

    [Test]
    public void WindowWithoutValueDefaultsToMacos()
    {
        // -d with no value defaults to macos
        var ok = OptionParser.TryParse(new[] { "-d" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("macos");
    }

    [Test]
    public void WindowWithoutValueFollowedByOptionDefaultsToMacos()
    {
        // -d followed by another option (not a window style) doesn't consume that option
        var ok = OptionParser.TryParse(new[] { "-d", "-w", "80" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("macos");
        options.Width.ShouldBe(80);
    }

    [Test]
    public void WindowWithSpaceSeparatedValueParsed()
    {
        // -d windows (space-separated) continues to work
        var ok = OptionParser.TryParse(new[] { "-d", "windows" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("windows");
    }

    [Test]
    public void NoLoopFlagDisablesLoop()
    {
        var ok = OptionParser.TryParse(
            new[] { "-m", "video", "--no-loop" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Video);
        options.Loop.ShouldBeFalse();
    }

    [Test]
    public void LoopDefaultIsTrue()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Loop.ShouldBeTrue();
    }

    [Test]
    public void FpsOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--fps", "24" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoFps.ShouldBe(24d);
    }

    [Test]
    public void InvalidFpsReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--fps", "0" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--fps must be greater than 0.");
    }

    [Test]
    public void WithCommandFlagParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--with-command", "ls" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.WithCommand.ShouldBeTrue();
        options.Command.ShouldBe("ls");
    }

    [Test]
    public void ShortFlagCMapsToWithCommand()
    {
        var ok = OptionParser.TryParse(new[] { "-c", "ls" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.WithCommand.ShouldBeTrue();
        options.Command.ShouldBe("ls");
    }

    [Test]
    public void LongFlagCommandIsUnknown()
    {
        var ok = OptionParser.TryParse(new[] { "--command", "ls" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
        error!.ShouldContain("Unknown option");
    }

    [Test]
    public void WithCommandDefaultIsFalse()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.WithCommand.ShouldBeFalse();
    }

    [Test]
    public void ShortFlagDMapsToWindow()
    {
        var ok = OptionParser.TryParse(new[] { "-d", "macos" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Window.ShouldBe("macos");
    }

    [Test]
    public void SleepOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--sleep", "2.5" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoSleep.ShouldBe(2.5d);
    }

    [Test]
    public void SleepDefaultIsTwo()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoSleep.ShouldBe(2d);
    }

    [Test]
    public void FadeOutOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--fadeout", "0.5" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoFadeOut.ShouldBe(0.5d);
    }

    [Test]
    public void FadeOutDefaultIsZero()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoFadeOut.ShouldBe(0d);
    }

    [Test]
    public void InvalidSleepReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--sleep", "-1" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--sleep must be a non-negative number.");
    }

    [Test]
    public void InvalidFadeOutReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--fadeout", "-0.5" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--fadeout must be a non-negative number.");
    }

    [Test]
    public void PaddingDefaultIsNullWhenNotSpecified()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Padding.ShouldBeNull();
    }

    [Test]
    public void PaddingExplicitlySetIsPreserved()
    {
        var ok = OptionParser.TryParse(new[] { "--padding", "5" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Padding.ShouldBe(5d);
    }

    [Test]
    public void OpacityOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--opacity", "0.5" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Opacity.ShouldBe(0.5d);
    }

    [Test]
    public void OpacityDefaultIsOne()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Opacity.ShouldBe(1d);
    }

    [Test]
    public void InvalidOpacityReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--opacity", "1.5" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--opacity must be a number between 0 and 1.");
    }

    [Test]
    public void BackgroundSingleColorParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--background", "#ff0000" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(1);
        options.Background[0].ShouldBe("#ff0000");
    }

    [Test]
    public void BackgroundTwoArgGradientParsed()
    {
        // --background "#from" "#to" syntax
        var ok = OptionParser.TryParse(
            new[] { "--background", "#ff0000", "#0000ff" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(2);
        options.Background[0].ShouldBe("#ff0000");
        options.Background[1].ShouldBe("#0000ff");
    }

    [Test]
    public void BackgroundColonGradientParsed()
    {
        // --background "#from:#to" syntax
        var ok = OptionParser.TryParse(
            new[] { "--background", "#ff0000:#0000ff" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(2);
        options.Background[0].ShouldBe("#ff0000");
        options.Background[1].ShouldBe("#0000ff");
    }

    [Test]
    public void BackgroundTwoFlagGradientParsed()
    {
        // --background c1 --background c2 legacy syntax
        var ok = OptionParser.TryParse(
            new[] { "--background", "#ff0000", "--background", "#0000ff" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(2);
        options.Background[0].ShouldBe("#ff0000");
        options.Background[1].ShouldBe("#0000ff");
    }

    [Test]
    public void BackgroundTwoArgDoesNotConsumeNonColorToken()
    {
        // Next token after --background value is a command-like string, should not be consumed
        var ok = OptionParser.TryParse(
            new[] { "--background", "#ff0000", "--", "echo", "hello" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(1);
        options.Background[0].ShouldBe("#ff0000");
        options!.Command.ShouldBe("echo hello");
    }

    [Test]
    public void BackgroundUrlNotSplitOnColon()
    {
        var ok = OptionParser.TryParse(
            new[] { "--background", "https://example.com/bg.png" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Background.Count.ShouldBe(1);
        options.Background[0].ShouldBe("https://example.com/bg.png");
    }

    [Test]
    public void ReplaySaveOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--replay-save", "replay.jsonl", "echo hi" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.ReplaySavePath.ShouldBe("replay.jsonl");
        options.Command.ShouldBe("echo hi");
    }

    [Test]
    public void ReplayOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--replay", "replay.jsonl", "echo hi" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.ReplayPath.ShouldBe("replay.jsonl");
        options.Command.ShouldBe("echo hi");
    }

    [Test]
    public void ReplayAndReplaySaveTogetherReturnsError()
    {
        var ok = OptionParser.TryParse(
            new[] { "--replay", "r.jsonl", "--replay-save", "s.jsonl", "echo hi" },
            out _,
            out var error,
            out _
        );
        ok.ShouldBeFalse();
        error.ShouldBe("--replay and --replay-save cannot be used together.");
    }

    [Test]
    public void ReplayWithoutCommandReturnsError()
    {
        var ok = OptionParser.TryParse(
            new[] { "--replay", "replay.jsonl" },
            out _,
            out var error,
            out _
        );
        ok.ShouldBeFalse();
        error.ShouldBe("--replay requires a command to be specified.");
    }

    [Test]
    public void ReplaySaveWithoutCommandReturnsError()
    {
        var ok = OptionParser.TryParse(
            new[] { "--replay-save", "replay.jsonl" },
            out _,
            out var error,
            out _
        );
        ok.ShouldBeFalse();
        error.ShouldBe("--replay-save requires a command to be specified.");
    }

    [Test]
    public void ReplayDefaultIsNull()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.ReplayPath.ShouldBeNull();
        options.ReplaySavePath.ShouldBeNull();
    }
}
