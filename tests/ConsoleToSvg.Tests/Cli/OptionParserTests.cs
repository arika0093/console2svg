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
        var ok = OptionParser.TryParse(new[] { "git log --oneline" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Command.ShouldBe("git log --oneline");
    }

    [Test]
    public void FontOptionParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--font", "Consolas, monospace" }, out var options, out _, out _);
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
    public void VerboseShortFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-v" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
    }

    [Test]
    public void VerboseLongFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "--verbose" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
    }

    [Test]
    public void VerboseFlagParsed()
    {
        var ok = OptionParser.TryParse(new[] { "-v" }, out var options, out _, out _);
        ok.ShouldBeTrue();
        options!.Verbose.ShouldBeTrue();
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
        var ok = OptionParser.TryParse(new[] { "-w", "80", "--", "dotnet", "test.cs" }, out var options, out _, out _);
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
}
