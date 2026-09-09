using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class ConsoleToSvgCommandLineTests
{
    [Test]
    public async Task LegacyRootInvocationMapsDelimitedCommandWithoutLosingArguments()
    {
        var invocation = await InvokeAsync("-w", "80", "--", "echo", "hello world");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options.ShouldNotBeNull();
        invocation.Options!.Workflow.ShouldBe(Workflow.Legacy);
        invocation.Options.Width.ShouldBe(80);
        invocation.Options.DelimitedCommand.ShouldBe(["echo", "hello world"]);
    }

    [Test]
    public async Task LegacyRootInputOptionMapsToAsciicastSource()
    {
        var invocation = await InvokeAsync("--in", "demo.cast", "-o", "demo.svg");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Legacy);
        invocation.Options.InputCastPath.ShouldBe("demo.cast");
        invocation.Options.OutputPath.ShouldBe("demo.svg");
    }

    [Test]
    public async Task InteractiveProgramRequiresAndPreservesDelimiter()
    {
        var invocation = await InvokeAsync("interactive", "--", "vim", "file name.txt");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Interactive.ShouldBeTrue();
        invocation.Options.DelimitedCommand.ShouldBe(["vim", "file name.txt"]);

        var invalid = await InvokeAsync("interactive", "vim");
        invalid.ExitCode.ShouldBe(1);
        invalid.Error.ShouldContain("must be specified after --");
    }

    [Test]
    public async Task OptionalWindowAndPathLikeVerboseValuesKeepTheirCustomSemantics()
    {
        var window = await InvokeAsync("capture", "-d", "--", "echo");
        window.ExitCode.ShouldBe(0);
        window.Options!.Window.ShouldBe("macos");

        var logPath = await InvokeAsync("capture", "--verbose", "logs/console2svg.log", "--", "echo");
        logPath.ExitCode.ShouldBe(0);
        logPath.Options!.Verbose.ShouldBeTrue();
        logPath.Options.VerboseLogPath.ShouldBe("logs/console2svg.log");

        var command = await InvokeAsync("capture", "--verbose", "echo");
        command.ExitCode.ShouldBe(0);
        command.Options!.VerboseLogPath.ShouldBeNull();
        command.Options.Command.ShouldBe("echo");
    }

    [Test]
    public async Task BackgroundGradientAndMaskPatternsUseConsoleToSvgSpecificMapping()
    {
        var invocation = await InvokeAsync(
            "capture",
            "--background",
            "#ff0000",
            "#0000ff",
            "--mask",
            "password",
            "token",
            "--",
            "echo"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Background.ShouldBe(["#ff0000", "#0000ff"]);
        invocation.Options.MaskPatterns.ShouldBe(["password", "token"]);

        var shorthand = await InvokeAsync("capture", "--background", "#ff0000:#0000ff", "--", "echo");
        shorthand.ExitCode.ShouldBe(0);
        shorthand.Options!.Background.ShouldBe(["#ff0000", "#0000ff"]);
    }

    [Test]
    public async Task SpecialTimeAndSizeFormatsMapToRenderOptions()
    {
        var invocation = await InvokeAsync(
            "capture",
            "--time",
            "1.5-3",
            "--size",
            "*x600",
            "--",
            "echo"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.TimeStart.ShouldBe(1.5d);
        invocation.Options.TimeEnd.ShouldBe(3d);
        invocation.Options.SizeWidth.ShouldBeNull();
        invocation.Options.SizeHeight.ShouldBe(600d);
    }

    [Test]
    public async Task CrossOptionValidationPreventsInvocation()
    {
        var invocation = await InvokeAsync("capture", "--frame", "1", "--time", "2", "--", "echo");

        invocation.ExitCode.ShouldBe(1);
        invocation.Options.ShouldBeNull();
        invocation.Error.ShouldContain("--time and --frame are mutually exclusive.");
    }

    [Test]
    public async Task StatusMapsTheJsonOption()
    {
        var invocation = await InvokeAsync("status", "--json");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Status);
        invocation.Options.StatusJson.ShouldBeTrue();
    }

    [Test]
    public async Task CastMapsTheAsciicastInput()
    {
        var invocation = await InvokeAsync("cast", "demo.cast");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Cast);
        invocation.Options.InputCastPath.ShouldBe("demo.cast");
    }

    [Test]
    public async Task TmuxCaptureMapsTheTmuxSpecificOptions()
    {
        var invocation = await InvokeAsync(
            "tmux",
            "capture",
            "--target",
            ":0",
            "--history",
            "1000",
            "-v"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Tmux);
        invocation.Options.RequestedTmuxAction.ShouldBe(TmuxAction.Capture);
        invocation.Options.TmuxTarget.ShouldBe(":0");
        invocation.Options.TmuxHistory.ShouldBeTrue();
        invocation.Options.TmuxHistoryLines.ShouldBe(1000);
        invocation.Options.Mode.ShouldBe(OutputMode.Video);
    }

    [Test]
    public async Task GeneratedHelpIsDecoratedByTheCustomAnsiFormatter()
    {
        var invocation = await InvokeAsync("capture", "--help");

        invocation.ExitCode.ShouldBe(0);
        invocation.Output.ShouldContain("\x1b[");
        invocation.Output.ShouldContain("Capture terminal output as SVG.");
        invocation.Output.ShouldContain("--width");
    }

    [Test]
    public async Task GeneratedHelpShowsOnlyOptionsAcceptedByEachWorkflow()
    {
        var liveServer = await InvokeAsync("live-server", "--help");
        liveServer.ExitCode.ShouldBe(0);
        liveServer.Output.ShouldNotContain("--interactive");
        liveServer.Output.ShouldNotContain("--frame");

        var completions = await InvokeAsync("completions", "script", "--help");
        completions.ExitCode.ShouldBe(0);
        completions.Output.ShouldNotContain("--interactive");
        completions.Output.ShouldNotContain("--svg-converter");

        var theme = await InvokeAsync("theme", "--help");
        theme.ExitCode.ShouldBe(0);
        theme.Output.ShouldNotContain("--interactive");
        theme.Output.ShouldNotContain("--svg-converter");
    }

    [Test]
    public async Task LiveServerEndpointIsOptionalAndAcceptsHostAndPort()
    {
        var defaultEndpoint = await InvokeAsync("live-server");
        defaultEndpoint.ExitCode.ShouldBe(0);
        defaultEndpoint.Options!.LiveServerPort.ShouldBe(38473);
        defaultEndpoint.Options.ListenAddress.ShouldBeNull();

        var portOnly = await InvokeAsync("live-server", "8080");
        portOnly.ExitCode.ShouldBe(0);
        portOnly.Options!.LiveServerPort.ShouldBe(8080);
        portOnly.Options.ListenAddress.ShouldBeNull();

        var hostAndPort = await InvokeAsync("live-server", "localhost:8080");
        hostAndPort.ExitCode.ShouldBe(0);
        hostAndPort.Options!.LiveServerPort.ShouldBe(8080);
        hostAndPort.Options.ListenAddress.ShouldBe("localhost");

        var endpointBeforeOption = await InvokeAsync(
            "live-server",
            "8081",
            "--theme",
            "cyberpunk-pc"
        );
        endpointBeforeOption.ExitCode.ShouldBe(0);
        endpointBeforeOption.Options!.LiveServerPort.ShouldBe(8081);
        endpointBeforeOption.Options.Themes.ShouldBe(["cyberpunk-pc"]);
    }

    [Test]
    public async Task LiveServerMapsThemeOptions()
    {
        var invocation = await InvokeAsync("live-server", "--theme", "cyberpunk-pc");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Themes.ShouldBe(["cyberpunk-pc"]);

        var renderOptions = SvgRenderOptionsFactory.Create(invocation.Options);
        renderOptions.TerminalTheme!.Foreground.ShouldBe("#d8f9ff");
        renderOptions.Background.ShouldBe(["#09051a", "#17104a"]);
        renderOptions.Chrome!.IsDesktop.ShouldBeTrue();
    }

    [Test]
    public async Task GeneratedHelpKeepsSvgConverterColumnCompact()
    {
        var invocation = await InvokeAsync("capture", "--help");

        invocation.ExitCode.ShouldBe(0);
        invocation.Output.ShouldContain("--svg-converter");
        invocation.Output.ShouldContain("<converter>");
        invocation.Output.ShouldNotContain("--svg-converter <auto|ffmpeg|resvg|rsvg|rsvg-convert>");
    }

    [Test]
    public async Task GeneratedCompletionScriptsUseLfLineEndings()
    {
        var bash = await InvokeAsync("completions", "script", "bash");
        var zsh = await InvokeAsync("completions", "script", "zsh");

        bash.ExitCode.ShouldBe(0);
        bash.Output.ShouldNotContain("\r");
        bash.Output.ShouldContain("--out -o");
        bash.Output.ShouldContain("--out|-o|--background|--verbose)");
        bash.Output.ShouldContain("compgen -f");
        zsh.ExitCode.ShouldBe(0);
        zsh.Output.ShouldNotContain("\r");
        zsh.Output.ShouldContain("--out=[Output file path.]:path:_files");
        zsh.Output.ShouldContain(
            "*--background=[Desktop background color or image. Can be specified twice for a gradient.]: :_files"
        );
        zsh.Output.ShouldContain("--verbose=[Enable verbose logging; optionally write to a file.]: :_files");
    }

    private static async Task<Invocation> InvokeAsync(params string[] args)
    {
        AppOptions? options = null;
        var commandLine = ConsoleToSvgCommandLine.Create(
            (parsed, _, _) =>
            {
                options = parsed;
                return Task.FromResult(0);
            }
        );
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await commandLine
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
        return new Invocation(exitCode, options, output.ToString(), error.ToString());
    }

    private sealed record Invocation(int ExitCode, AppOptions? Options, string Output, string Error);
}
