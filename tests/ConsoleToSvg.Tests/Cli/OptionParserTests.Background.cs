using System;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Cli;

public sealed partial class OptionParserTests
{
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

    [Test]
    public void PcModeFlagSetsPcMode()
    {
        var ok = OptionParser.TryParse(new[] { "--pcmode" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.PcMode.ShouldBeTrue();
    }

    [Test]
    public void PcModeDefaultIsFalse()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.PcMode.ShouldBeFalse();
    }

    [Test]
    public void PcPaddingOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--pc-padding", "30" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.PcPadding.ShouldBe(30d);
    }

    [Test]
    public void PcPaddingDefaultIsNull()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.PcPadding.ShouldBeNull();
    }

    [Test]
    public void BackColorOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--backcolor", "#0c0c0c" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.BackColor.ShouldBe("#0c0c0c");
    }

    [Test]
    public void BackColorDefaultIsNull()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.BackColor.ShouldBeNull();
    }

    [Test]
    public void BackColorRequiresValue()
    {
        var ok = OptionParser.TryParse(new[] { "--backcolor" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Test]
    public void PcModeWithMacosWindowUsesMacosPc()
    {
        // When --pcmode is set with --window macos, FromAppOptions should produce a desktop chrome.
        var ok = OptionParser.TryParse(
            new[] { "--window", "macos", "--pcmode" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.PcMode.ShouldBeTrue();
        options.Window.ShouldBe("macos");
        // SvgRenderOptions.FromAppOptions resolves the effective window name
        var svgOptions = SvgRenderOptionsFactory.Create(options!);
        // The chrome should have IsDesktop = true (macos + --pcmode -> macos-pc path)
        svgOptions.Chrome.ShouldNotBeNull();
        svgOptions.Chrome!.IsDesktop.ShouldBeTrue();
    }

    [Test]
    public void PcModeDoesNotDoubleAppendPcSuffix()
    {
        // When --pcmode is set with --window macos-pc, should not become macos-pc-pc
        var ok = OptionParser.TryParse(
            new[] { "--window", "macos-pc", "--pcmode" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var svgOptions = SvgRenderOptionsFactory.Create(options!);
        svgOptions.Chrome.ShouldNotBeNull();
        svgOptions.Chrome!.IsDesktop.ShouldBeTrue();
    }

    [Test]
    public void PcPaddingOverridesDesktopPadding()
    {
        var ok = OptionParser.TryParse(
            new[] { "--window", "macos-pc", "--pc-padding", "40" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var svgOptions = SvgRenderOptionsFactory.Create(options!);
        svgOptions.Chrome.ShouldNotBeNull();
        svgOptions.Chrome!.DesktopPadding.ShouldBe(40d);
    }

    [Test]
    public void PcModeWorksForTransparentStyle()
    {
        // --pcmode should work with any built-in style, not just macos/windows
        var ok = OptionParser.TryParse(
            new[] { "--window", "transparent", "--pcmode" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var svgOptions = SvgRenderOptionsFactory.Create(options!);
        // transparent-pc resolves to transparent base with IsDesktop=true
        svgOptions.Chrome.ShouldNotBeNull();
        svgOptions.Chrome!.IsDesktop.ShouldBeTrue();
    }

    [Test]
    public void SvgRenderOptionsCarriesVideoTiming()
    {
        var ok = OptionParser.TryParse(
            new[] { "--timing", "realtime" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();

        var renderOptions = SvgRenderOptionsFactory.Create(options!);
        renderOptions.VideoTiming.ShouldBe(VideoTimingMode.Realtime);
    }

    [Test]
    public void RepeatModeParsedFromLongFlag()
    {
        var ok = OptionParser.TryParse(
            new[] { "-m", "repeat", "--", "echo", "hello" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Repeat);
        options.Command.ShouldBe("echo hello");
    }

    [Test]
    public void RepeatModeParsedCaseInsensitive()
    {
        var ok = OptionParser.TryParse(
            new[] { "--mode", "REPEAT", "--", "echo", "hi" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.Mode.ShouldBe(OutputMode.Repeat);
    }

    [Test]
    public void RepeatModeWithoutCommandReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--mode", "repeat" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--mode repeat requires a command to be specified.");
    }

    [Test]
    public void InvalidModeReturnsUpdatedError()
    {
        var ok = OptionParser.TryParse(new[] { "--mode", "unknown" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--mode must be image, video, or repeat.");
    }

    [Test]
    public void SaveFramesOptionParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--save-frames", "frames-out" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SaveFramesDir.ShouldBe("frames-out");
    }

    [Test]
    public void SaveFramesDefaultIsNull()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SaveFramesDir.ShouldBeNull();
    }

    [Test]
    public void SaveFramesRequiresValue()
    {
        var ok = OptionParser.TryParse(new[] { "--save-frames" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Test]
    public void OutputPathDefaultIsSvg()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.OutputPath.ShouldBe("output.svg");
    }

    [Test]
    public void OutputPathPngParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-o", "output.png" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.OutputPath.ShouldBe("output.png");
    }

    [Test]
    public void OutputPathMp4Parsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--out", "output.mp4" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.OutputPath.ShouldBe("output.mp4");
    }

    [Test]
    public void OutputPathWebmParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "-o", "output.webm" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.OutputPath.ShouldBe("output.webm");
    }

    [Test]
    public void SizeWidthOnlyParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "800" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBe(800d);
        options.SizeHeight.ShouldBeNull();
    }

    [Test]
    public void SizeWidthOnlyWithAsteriskParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "800x*" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBe(800d);
        options.SizeHeight.ShouldBeNull();
    }

    [Test]
    public void SizeHeightOnlyWithAsteriskParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "*x600" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBeNull();
        options.SizeHeight.ShouldBe(600d);
    }

    [Test]
    public void SizeBothDimensionsParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--size", "800x600" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBe(800d);
        options.SizeHeight.ShouldBe(600d);
    }

    [Test]
    public void SizeBothDimensionsWithDecimalsParsed()
    {
        var ok = OptionParser.TryParse(
            new[] { "--size", "1920x1080" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBe(1920d);
        options.SizeHeight.ShouldBe(1080d);
    }

    [Test]
    public void SizeDefaultIsNull()
    {
        var ok = OptionParser.TryParse(System.Array.Empty<string>(), out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.SizeWidth.ShouldBeNull();
        options.SizeHeight.ShouldBeNull();
    }

    [Test]
    public void SizeInvalidWidthReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "abcx600" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Test]
    public void SizeInvalidHeightReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "800xabc" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Test]
    public void SizeZeroWidthReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "0x600" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--size width must be greater than 0.");
    }

    [Test]
    public void SizeZeroHeightReturnsError()
    {
        var ok = OptionParser.TryParse(new[] { "--size", "800x0" }, out _, out var error, out _);
        ok.ShouldBeFalse();
        error.ShouldBe("--size height must be greater than 0.");
    }

    [Test]
    public void SizeWithoutNumericDimensionsReturnsError()
    {
        foreach (var value in new[] { "x", "x*", "*x", "*x*" })
        {
            var ok = OptionParser.TryParse(new[] { "--size", value }, out _, out var error, out _);
            ok.ShouldBeFalse();
            error.ShouldBe("--size must specify at least one numeric dimension.");
        }
    }

    [Test]
    public void SizePassedThroughToSvgRenderOptions()
    {
        var ok = OptionParser.TryParse(
            new[] { "--size", "1000x500" },
            out var options,
            out _,
            out _
        );
        ok.ShouldBeTrue();
        var renderOptions = SvgRenderOptionsFactory.Create(options!);
        renderOptions.SizeWidth.ShouldBe(1000d);
        renderOptions.SizeHeight.ShouldBe(500d);
    }
}
