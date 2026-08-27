using ConsoleToSvg.Cli;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Cli;

public sealed class CliValueParserTests
{
    [Test]
    public void TerminalDimensionAcceptsFixedAndAdjustValues()
    {
        TerminalDimensionParserAttribute.TryParse("120", out var fixedSize).ShouldBeTrue();
        TerminalDimensionParserAttribute.TryParse("adjust", out var adjusted).ShouldBeTrue();

        fixedSize.Value.ShouldBe(120);
        fixedSize.Adjust.ShouldBeFalse();
        adjusted.Value.ShouldBeNull();
        adjusted.Adjust.ShouldBeTrue();
    }

    [Test]
    public void TimeSelectionAcceptsSingleAndRangeValues()
    {
        TimeSelectionParserAttribute.TryParse("1.5", out var single).ShouldBeTrue();
        TimeSelectionParserAttribute.TryParse("1.5-3.0", out var range).ShouldBeTrue();

        single.Kind.ShouldBe(TimeSelectionKind.Single);
        single.Value.ShouldBe(1.5);
        range.Kind.ShouldBe(TimeSelectionKind.Range);
        range.Start.ShouldBe(1.5);
        range.End.ShouldBe(3.0);
    }

    [Test]
    public void OutputSizeAndConverterUseCanonicalSyntax()
    {
        OutputSizeParserAttribute.TryParse("800x*", out var size).ShouldBeTrue();
        SvgConverterModeParserAttribute.TryParse("rsvg-convert", out var converter)
            .ShouldBeTrue();

        size.Width.ShouldBe(800);
        size.Height.ShouldBeNull();
        converter.ShouldBe(SvgConverterMode.RsvgConvert);
    }

    [Test]
    public void ExplicitFactoryPreservesCommandBoundariesAndRenderingValues()
    {
        var options = AppOptionsFactory.CreateRendered(
            CliVerb.Capture,
            new RenderCommandSettings
            {
                Format = "png",
                Width = new TerminalDimension(90),
                Height = new TerminalDimension(30),
                Background = ["#111111:#222222"],
                Size = new OutputSize(640, null),
            },
            inputCastPath: null,
            replayPath: null,
            escapedCommand: ["printf", "hello world"]
        );

        AppOptionsValidator.TryFinalize(options, out var error).ShouldBeTrue(error);
        options.OutputPath.ShouldBe("output.png");
        options.Width.ShouldBe(90);
        options.Height.ShouldBe(30);
        options.Background.ShouldBe(["#111111", "#222222"]);
        options.SizeWidth.ShouldBe(640);
        options.DelimitedCommand.ShouldBe(["printf", "hello world"]);
        options.Command.ShouldBe("printf hello world");
    }

    [Test]
    public void LegacyAdapterNormalizesFlatHeightAndDelimitedCommand()
    {
        LegacyArgumentAdapter
            .Normalize(["-w", "100", "-h", "25", "--", "printf", "legacy"])
            .ShouldBe(
                ["capture", "-w", "100", "--height", "25", "--", "printf", "legacy"]
            );
    }
}
