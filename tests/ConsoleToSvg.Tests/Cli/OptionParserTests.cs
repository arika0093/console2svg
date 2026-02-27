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
        var ok = OptionParser.TryParse(new[] { "--window", "linux" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--window must be none, macos, windows, macos-pc, or windows-pc.");
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
        var ok = OptionParser.TryParse(
            new[] { "-c", "ls" },
            out var options,
            out _,
            out _
        );
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
}
