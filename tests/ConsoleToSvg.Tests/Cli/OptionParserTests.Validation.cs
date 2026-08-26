using System;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Cli;

public sealed partial class OptionParserTests
{
    [Test]
    public void EmbedCastRejectsNonSvgOutput()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-cast", "--out", "output.png" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("Embed options require SVG output.");
    }

    [Test]
    public void EmbedReplayRequiresCommand()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-replay" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("--embed-replay requires a command to be specified.");
    }

    [Test]
    public void EmbedDebugInheritsEmbedReplayCommandRequirement()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-debug" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("--embed-replay requires a command to be specified.");
    }

    [Test]
    public void EmbedReplayRejectsReplayInput()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-replay", "--replay", "input.json", "echo hi" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("--embed-replay and --replay cannot be used together.");
    }

    [Test]
    public void EmbedReplayRejectsRepeatMode()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-replay", "--mode", "repeat", "echo hi" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("--embed-replay cannot be used with --mode repeat.");
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
    public void NoColorEnvFlagEnablesNoColorEnv()
    {
        var ok = OptionParser.TryParse(new[] { "--no-colorenv" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.NoColorEnv.ShouldBeTrue();
    }

    [Test]
    public void NoColorEnvDefaultIsFalse()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.NoColorEnv.ShouldBeFalse();
    }

    [Test]
    public void NoDeleteEnvsFlagEnablesNoDeleteEnvs()
    {
        var ok = OptionParser.TryParse(new[] { "--no-delete-envs" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.NoDeleteEnvs.ShouldBeTrue();
    }

    [Test]
    public void NoDeleteEnvsDefaultIsFalse()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.NoDeleteEnvs.ShouldBeFalse();
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
    public void HeaderOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--header", "custom header" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Header.ShouldBe("custom header");
    }

    [Test]
    public void PromptOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--prompt", "#" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Prompt.ShouldBe("#");
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
    public void SleepDefaultIsZeri()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoSleep.ShouldBe(0d);
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
    public void VideoTimingDefaultIsDeterministic()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.VideoTiming.ShouldBe(VideoTimingMode.Deterministic);
    }

    [Test]
    public void VideoTimingRealtimeParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--timing", "realtime" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.VideoTiming.ShouldBe(VideoTimingMode.Realtime);
    }

    [Test]
    public void InvalidVideoTimingReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--timing", "foo" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--timing must be deterministic or realtime.");
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
    public void InvalidAdjustReturnsError()
    {
        var ok = OptionParser.TryParse(
            new[] { "--adjust", "invalid" },
            out _,
            out var error,
            out _
        );
        ok.ShouldBeFalse();
        error.ShouldBe("--adjust must be spacing or spacingAndGlyphs.");
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
}
