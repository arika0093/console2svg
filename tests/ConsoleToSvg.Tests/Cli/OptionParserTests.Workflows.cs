using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed partial class OptionParserTests
{
    [Test]
    public void CaptureForwardsDelimitedArguments()
    {
        OptionParser.TryParse(["capture", "-w", "80", "--", "dotnet", "--version"], out var options, out _, out _).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Capture);
        options.DelimitedCommand.ShouldBe(["dotnet", "--version"]);
    }

    [Test]
    public void InteractiveVerbEnablesInteractiveMode()
    {
        OptionParser.TryParse(["interactive", "-o", "capture.svg"], out var options, out _, out _).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Interactive);
        options.Interactive.ShouldBeTrue();
    }

    [Test]
    public void ReplayVerbOwnsReplayFile()
    {
        OptionParser.TryParse(["replay", "keys.json", "--", "bash", "-l"], out var options, out _, out _).ShouldBeTrue();
        options!.ReplayPath.ShouldBe("keys.json");
        options.DelimitedCommand.ShouldBe(["bash", "-l"]);
    }

    [Test]
    public void ConvertVerbOwnsInputCast()
    {
        OptionParser.TryParse(["convert", "demo.cast", "-o", "demo.png"], out var options, out _, out _).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Convert);
        options.InputCastPath.ShouldBe("demo.cast");
        options.OutputPath.ShouldBe("demo.png");
    }

    [Test]
    public void ConvertVerbAcceptsSvgInput()
    {
        OptionParser.TryParse(["convert", "demo.svg", "-o", "demo.png"], out var options, out _, out _).ShouldBeTrue();
        options!.InputSvgPath.ShouldBe("demo.svg");
        options.InputCastPath.ShouldBeNull();
    }

    [Test]
    public void ConvertSvgRejectsACommand()
    {
        OptionParser.TryParse(["convert", "demo.svg", "--", "echo", "hello"], out _, out var error, out _).ShouldBeFalse();
        error.ShouldBe("--command and --in cannot be used together.");
    }

    [Test]
    public void ConvertSvgRejectsAnAsciicastInput()
    {
        OptionParser.TryParse(["convert", "demo.svg", "--in", "other.cast"], out _, out var error, out _).ShouldBeFalse();
        error.ShouldBe("SVG input and --in cannot be used together.");
    }

    [Test]
    public void ConvertSvgRejectsInteractiveMode()
    {
        OptionParser.TryParse(["convert", "demo.svg", "--interactive"], out _, out var error, out _).ShouldBeFalse();
        error.ShouldBe("--interactive cannot be used with SVG input.");
    }

    [Test]
    public void ConvertSvgRejectsVideoOutput()
    {
        OptionParser.TryParse(["convert", "demo.svg", "-o", "demo.mp4"], out _, out var error, out _).ShouldBeFalse();
        error.ShouldBe("SVG input cannot be converted to video output.");
    }

    [Test]
    public void LegacyInvocationStillWorks()
    {
        OptionParser.TryParse(["-w", "80", "--", "echo", "hello"], out var options, out _, out _).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Legacy);
        options.Command.ShouldBe("echo hello");
    }

    [Test]
    public void LegacyInputAndReplayOptionsStillWork()
    {
        OptionParser.TryParse(["--in", "demo.cast", "-o", "demo.svg"], out var inputOptions, out _, out _).ShouldBeTrue();
        inputOptions!.Workflow.ShouldBe(Workflow.Legacy);
        inputOptions.InputCastPath.ShouldBe("demo.cast");

        OptionParser.TryParse(["--replay", "keys.json", "--", "bash", "-l"], out var replayOptions, out _, out _).ShouldBeTrue();
        replayOptions!.Workflow.ShouldBe(Workflow.Legacy);
        replayOptions.ReplayPath.ShouldBe("keys.json");
        replayOptions.DelimitedCommand.ShouldBe(["bash", "-l"]);
    }

    [Test]
    public void ThemeCommandIsReserved()
    {
        OptionParser.TryParse(["theme"], out var options, out _, out var showHelp).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Theme);
        showHelp.ShouldBeTrue();
        OptionParser.GetHelpText(Workflow.Theme).ShouldContain("#115");
    }

    [Test]
    public void ReservedThemeSubcommandReturnsActionableError()
    {
        OptionParser.TryParse(["theme", "list"], out _, out var error, out _).ShouldBeFalse();
        error!.ShouldContain("reserved");
        error!.ShouldContain("--theme");
    }

    [Test]
    public void VerbHelpIsScoped()
    {
        OptionParser.TryParse(["replay", "--help"], out var options, out _, out var showHelp).ShouldBeTrue();
        options!.Workflow.ShouldBe(Workflow.Replay);
        showHelp.ShouldBeTrue();
        OptionParser.GetHelpText(Workflow.Replay).ShouldContain("replay <replay.json>");
    }

    [Test]
    public void RootHelpDocumentsOnlyVerbSyntax()
    {
        OptionParser.HelpText.ShouldNotContain("Legacy:");
        OptionParser.HelpText.ShouldNotContain("console2svg [options]");
    }
}
