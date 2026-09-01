using System;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Cli;

public sealed partial class OptionParserTests
{
    [Test]
    public void MaskAutoEnablesAllCategoriesByDefault()
    {
        var ok = OptionParser.TryParse([], out var options, out _, out _);

        ok.ShouldBeTrue();
        options!.AutoMask.ShouldBe(
            AutoMaskCategory.Password | AutoMaskCategory.Token | AutoMaskCategory.HomeDirectory
        );
        SvgRenderOptionsFactory
            .Create(options)
            .AutoMaskHomeDirectory.ShouldBe(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            );
    }

    [Test]
    public void MaskAutoParsesCommaSeparatedCategories()
    {
        var ok = OptionParser.TryParse(
            new[] { "--mask-auto", "password,token,homedir" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.AutoMask.ShouldBe(
            AutoMaskCategory.Password | AutoMaskCategory.Token | AutoMaskCategory.HomeDirectory
        );
        var renderOptions = SvgRenderOptionsFactory.Create(options);
        renderOptions.AutoMask.ShouldBe(options.AutoMask);
        renderOptions.AutoMaskHomeDirectory.ShouldBe(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );
    }

    [Test]
    public void MaskAutoNoneDisablesAllCategories()
    {
        var ok = OptionParser.TryParse(new[] { "--mask-auto=none" }, out var options, out _, out _);

        ok.ShouldBeTrue();
        options!.AutoMask.ShouldBe(AutoMaskCategory.None);
        SvgRenderOptionsFactory.Create(options).AutoMaskHomeDirectory.ShouldBeNull();
    }

    [Test]
    public void MaskAutoRejectsUnknownOrMixedNoneCategories()
    {
        OptionParser
            .TryParse(new[] { "--mask-auto", "email" }, out _, out var unknownError, out _)
            .ShouldBeFalse();
        unknownError.ShouldNotBeNull();
        unknownError.ShouldContain("Unknown --mask-auto category");

        OptionParser
            .TryParse(new[] { "--mask-auto", "none,token" }, out _, out var mixedError, out _)
            .ShouldBeFalse();
        mixedError.ShouldBe("--mask-auto none cannot be combined with other categories.");
    }

    [Test]
    public void EmbedCastOptionIsEnabledWithoutAValue()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-cast", "echo hi" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.EmbedCast.ShouldBeTrue();
        options.Command.ShouldBe("echo hi");
    }

    [Test]
    public void EmbedLogsOptionIsEnabledWithoutVerbose()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-logs", "echo hi" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.EmbedLogs.ShouldBeTrue();
        options.Verbose.ShouldBeFalse();
    }

    [Test]
    public void EmbedReplayOptionIsEnabledForACommand()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-replay", "echo hi" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.EmbedReplay.ShouldBeTrue();
    }

    [Test]
    public void EmbedDebugEnablesAllEmbedOptions()
    {
        var ok = OptionParser.TryParse(
            new[] { "--embed-debug", "echo hi" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.EmbedDebug.ShouldBeTrue();
        options.EmbedCast.ShouldBeTrue();
        options.EmbedLogs.ShouldBeTrue();
        options.EmbedReplay.ShouldBeTrue();
    }

    [Test]
    public void InteractiveModeIsEnabled()
    {
        var ok = OptionParser.TryParse(new[] { "--interactive" }, out var options, out _, out _);

        ok.ShouldBeTrue();
        options!.Interactive.ShouldBeTrue();
        options.WidthAdjust.ShouldBeTrue();
        options.HeightAdjust.ShouldBeTrue();
    }

    [Test]
    public void InteractiveModePreservesExplicitDimensions()
    {
        var ok = OptionParser.TryParse(
            new[] { "--interactive", "-w", "120", "-h", "30" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.Width.ShouldBe(120);
        options.WidthAdjust.ShouldBeFalse();
        options.Height.ShouldBe(30);
        options.HeightAdjust.ShouldBeFalse();
    }

    [Test]
    public void InteractiveHelpDocumentsF9RecordingAndF10Screenshot()
    {
        OptionParser.HelpText.ShouldContain("F9 starts/stops an animation recording");
        OptionParser.HelpText.ShouldContain("F10 saves a static screenshot");
    }

    [Test]
    public void FullHelpGroupsCastReplayAndVerboseOptions()
    {
        const string section = "Options (Recording and replay):";
        var sectionStart = OptionParser.HelpText.IndexOf(section, StringComparison.Ordinal);
        var nextSection = OptionParser.HelpText.IndexOf(
            "Options (Appearance):",
            sectionStart,
            StringComparison.Ordinal
        );

        sectionStart.ShouldBeGreaterThanOrEqualTo(0);
        nextSection.ShouldBeGreaterThan(sectionStart);
        var recordingSection = OptionParser.HelpText[sectionStart..nextSection];
        recordingSection.ShouldContain("--in <path>");
        recordingSection.ShouldContain("--save-cast <path>");
        recordingSection.ShouldContain("--embed-cast");
        recordingSection.ShouldContain("--embed-logs");
        recordingSection.ShouldContain("--embed-replay");
        recordingSection.ShouldContain("--embed-debug");
        recordingSection.ShouldContain("--replay <path>");
        recordingSection.ShouldContain("--replay-save <path>");
        recordingSection.ShouldContain("--verbose [path]");
    }

    [Test]
    public void InteractiveModeRejectsRemovedKeybindOption()
    {
        var ok = OptionParser.TryParse(
            new[] { "--interactive", "--keybind", "Ctrl+G" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("Unknown option: --keybind");
    }

    [Test]
    [Arguments(
        new[] { "--interactive", "echo hi" },
        "An interactive program must be specified after -- (for example: -i -- vim)."
    )]
    [Arguments(
        new[] { "--interactive", "--in", "record.cast" },
        "--interactive cannot be used with --in."
    )]
    [Arguments(
        new[] { "--interactive", "--stdout" },
        "--interactive cannot be used with --stdout."
    )]
    [Arguments(
        new[] { "--interactive", "--save-cast", "trace.cast" },
        "--interactive cannot be used with --save-cast."
    )]
    [Arguments(
        new[] { "--interactive", "--mode", "repeat" },
        "--interactive cannot be used with --mode repeat."
    )]
    [Arguments(
        new[] { "--interactive", "--replay", "input.json" },
        "--interactive cannot be used with replay options."
    )]
    public void InteractiveModeRejectsIncompatibleSources(string[] args, string expectedError)
    {
        var ok = OptionParser.TryParse(args, out _, out var error, out _);

        ok.ShouldBeFalse();
        error.ShouldBe(expectedError);
    }

    [Test]
    public void InteractiveModeRejectsEmbedCast()
    {
        var ok = OptionParser.TryParse(
            new[] { "--interactive", "--embed-cast" },
            out _,
            out var error,
            out _
        );

        ok.ShouldBeFalse();
        error.ShouldBe("--interactive cannot be used with --embed-cast.");
    }

    [Test]
    public void InteractiveModeStartsDelimitedProgramWithUnmodifiedArguments()
    {
        var ok = OptionParser.TryParse(
            new[] { "-i", "--", "vim", "file name.txt" },
            out var options,
            out _,
            out _
        );

        ok.ShouldBeTrue();
        options!.Command.ShouldBe("vim file name.txt");
        options.DelimitedCommand.ShouldBe(new[] { "vim", "file name.txt" });
    }

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
    public void ForeColorOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--forecolor", "#ff00ff" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.ForeColor.ShouldBe("#ff00ff");
    }

    [Test]
    public void AdjustOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--adjust", "spacingAndGlyphs" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.LengthAdjust.ShouldBe("spacingAndGlyphs");
    }

    [Test]
    public void SvgConverterDefaultsToAuto()
    {
        var ok = OptionParser.TryParse(Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SvgConverter.ShouldBe(SvgConverterMode.Auto);
    }

    [Test]
    public void OutputCoalescingDefaultsToAuto()
    {
        var ok = OptionParser.TryParse(Array.Empty<string>(), out var options, out _, out _);

        ok.ShouldBeTrue();
        options!.OutputCoalesceMs.ShouldBeNull();
    }

    [Test]
    public void OutputCoalescingAcceptsAutoAndNumericOverrides()
    {
        var autoOk = OptionParser.TryParse(
            new[] { "--coalesce-ms", "auto" },
            out var autoOptions,
            out _,
            out _
        );
        var numericOk = OptionParser.TryParse(
            new[] { "--coalesce-ms", "0" },
            out var numericOptions,
            out _,
            out _
        );

        autoOk.ShouldBeTrue();
        autoOptions!.OutputCoalesceMs.ShouldBeNull();
        numericOk.ShouldBeTrue();
        numericOptions!.OutputCoalesceMs.ShouldBe(0d);
    }

    [Test]
    public void SvgConverterFfmpegParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--svg-converter", "ffmpeg" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SvgConverter.ShouldBe(SvgConverterMode.Ffmpeg);
    }

    [Test]
    public void SvgConverterRsvgConvertParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--svg-converter", "rsvg-convert" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SvgConverter.ShouldBe(SvgConverterMode.RsvgConvert);
    }

    [Test]
    public void SvgConverterResvgParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--svg-converter", "resvg" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SvgConverter.ShouldBe(SvgConverterMode.Resvg);
    }

    [Test]
    public void SvgConverterInvalidValueRejected()
    {
        var ok = OptionParser.TryParse(
            new[] { "--svg-converter", "foobar" },
            out _,
            out var error,
            out _
        );
        ok.ShouldBeFalse();
        error!.ShouldContain("svg-converter");
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
        options.VerboseLogPath.ShouldBeNull();
    }

    [Test]
    public void VerboseWithLogPathParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--verbose", "debug.log" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
        options.VerboseLogPath.ShouldBe("debug.log");
    }

    [Test]
    public void VerboseWithAbsolutePathParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--verbose", "/tmp/console2svg.log" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
        options.VerboseLogPath.ShouldBe("/tmp/console2svg.log");
    }

    [Test]
    public void VerboseWithInlinePathParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--verbose=./output.log" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
        options.VerboseLogPath.ShouldBe("./output.log");
    }

    [Test]
    public void VerboseWithoutPathDoesNotConsumeNextArg()
    {
        // --verbose followed by a plain command-like token (no path chars/extension)
        var ok = OptionParser.TryParse(
            new[] { "--verbose", "mycommand" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
        options.VerboseLogPath.ShouldBeNull();
        options.Command.ShouldBe("mycommand");
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
    public void WidthAdjustAndHeightAdjustAreTrueByDefault()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.WidthAdjust.ShouldBeTrue();
        options.HeightAdjust.ShouldBeTrue();
    }

    [Test]
    public void ExplicitNumericWidthSetsWidthAdjustFalse()
    {
        var ok = OptionParser.TryParse(new[] { "-w", "80" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Width.ShouldBe(80);
        options.WidthAdjust.ShouldBeFalse();
    }

    [Test]
    public void ExplicitNumericHeightSetsHeightAdjustFalse()
    {
        var ok = OptionParser.TryParse(new[] { "-h", "24" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Height.ShouldBe(24);
        options.HeightAdjust.ShouldBeFalse();
    }

    [Test]
    public void WidthAdjustParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--width", "adjust" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.WidthAdjust.ShouldBeTrue();
        options.Width.ShouldBeNull();
    }

    [Test]
    public void HeightAdjustParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--height", "adjust" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.HeightAdjust.ShouldBeTrue();
        options.Height.ShouldBeNull();
    }

    [Test]
    public void WidthAdjustShortFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-w", "adjust" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.WidthAdjust.ShouldBeTrue();
        options.Width.ShouldBeNull();
    }

    [Test]
    public void HeightAdjustShortFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-h", "adjust" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.HeightAdjust.ShouldBeTrue();
        options.Height.ShouldBeNull();
    }

    [Test]
    public void WidthAdjustCaseInsensitive()
    {
        var ok = OptionParser.TryParse(
            new[] { "--width", "ADJUST" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.WidthAdjust.ShouldBeTrue();
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
}
