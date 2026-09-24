using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Tests.Cli;

public sealed class ConsoleToSvgCommandLineTests
{
    [Test]
    public void VerboseLogPathDefaultsToTimestampedNameAndPreservesExplicitPath()
    {
        var defaultPath = Program.ResolveVerboseLogPath(null);

        Path.GetFileName(defaultPath).ShouldStartWith("console2svg_");
        Path.GetExtension(defaultPath).ShouldBe(".log");
        DateTime.TryParseExact(
                Path.GetFileNameWithoutExtension(defaultPath)["console2svg_".Length..],
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _
            )
            .ShouldBeTrue();
        Program.ResolveVerboseLogPath("custom.log").ShouldBe("custom.log");
    }

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

        var logPath = await InvokeAsync(
            "capture",
            "--verbose",
            "logs/console2svg.log",
            "--",
            "echo"
        );
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

        var shorthand = await InvokeAsync(
            "capture",
            "--background",
            "#ff0000:#0000ff",
            "--",
            "echo"
        );
        shorthand.ExitCode.ShouldBe(0);
        shorthand.Options!.Background.ShouldBe(["#ff0000", "#0000ff"]);
    }

    [Test]
    public async Task AutomaticMaskingIsEnabledByDefaultOutsideMarkdownBatchMode()
    {
        var defaultInvocation = await InvokeAsync("capture", "--", "echo");
        var disabledInvocation = await InvokeAsync(
            "capture",
            "--mask-auto",
            "false",
            "--",
            "echo"
        );

        defaultInvocation.Options!.MaskAuto.ShouldBeTrue();
        defaultInvocation.Options.IsMaskAutoExplicit.ShouldBeFalse();
        disabledInvocation.Options!.MaskAuto.ShouldBeFalse();
        disabledInvocation.Options.IsMaskAutoExplicit.ShouldBeTrue();
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
        invocation.Options.OutputFormat.ShouldBe(OutputFormat.Json);
    }

    [Test]
    public async Task LlmSkillsWritesOnlyTheBundledAgentSkill()
    {
        var invocation = await InvokeAsync("llm", "skills");

        invocation.ExitCode.ShouldBe(0);
        invocation.Error.ShouldBeEmpty();
        invocation.Output.ShouldStartWith("---\nname: console2svg\n");
        invocation.Output.ShouldContain("description:");
        invocation.Output.ShouldContain("## One-shot command capture");
        invocation.Output.ShouldContain("## Managed TUI sessions");
        invocation.Output.ShouldEndWith("\n");
        invocation.Output.ShouldNotContain("\x1b[");
    }

    [Test]
    public async Task LlmHelpDescribesSkillsCommand()
    {
        var invocation = await InvokeAsync("llm", "--help");

        invocation.ExitCode.ShouldBe(0);
        invocation.Output.ShouldContain("LLM integration helpers.");
        invocation.Output.ShouldContain("skills");
        invocation.Output.ShouldContain("Agent Skill");
    }

    [Test]
    public async Task StatusAndThemeListMapTheSharedOutputFormats()
    {
        var status = await InvokeAsync("status", "--format", "markdown");
        var themes = await InvokeAsync("theme", "list", "--format", "json");

        status.ExitCode.ShouldBe(0);
        status.Options!.OutputFormat.ShouldBe(OutputFormat.Markdown);
        themes.ExitCode.ShouldBe(0);
        themes.Options!.Workflow.ShouldBe(Workflow.Theme);
        themes.Options.OutputFormat.ShouldBe(OutputFormat.Json);
    }

    [Test]
    public async Task FormatOptionOverridesTheOutputExtension()
    {
        var defaultName = await InvokeAsync("capture", "--format", "png", "--", "echo");
        defaultName.ExitCode.ShouldBe(0);
        defaultName.Options!.Format.ShouldBe("png");
        defaultName.Options.IsFormatExplicit.ShouldBeTrue();
        defaultName.Options.OutputPath.ShouldBe("output.png");

        var explicitName = await InvokeAsync(
            "capture",
            "--format",
            "gif",
            "-o",
            "result.svg",
            "--",
            "echo"
        );
        explicitName.ExitCode.ShouldBe(0);
        explicitName.Options!.Format.ShouldBe("gif");
        explicitName.Options.OutputPath.ShouldBe("result.gif");

        var jpeg = await InvokeAsync("capture", "--format", "jpeg", "--", "echo");
        jpeg.Options!.Format.ShouldBe("jpg");
        jpeg.Options.OutputPath.ShouldBe("output.jpg");
    }

    [Test]
    public async Task StdoutRejectsNonSvgFormats()
    {
        var invocation = await InvokeAsync(
            "capture",
            "--stdout",
            "--format",
            "png",
            "--",
            "echo"
        );

        invocation.ExitCode.ShouldBe(1);
        invocation.Options.ShouldBeNull();
        invocation.Error.ShouldContain("--stdout only supports SVG output.");
    }

    [Test]
    public async Task LiveServerDoesNotExposeTheFormatOption()
    {
        var help = await InvokeAsync("live-server", "--help");

        help.ExitCode.ShouldBe(0);
        help.Output.ShouldNotContain("--format");
    }

    [Test]
    public async Task UpdateMapsCheckForceAndYesOptions()
    {
        var invocation = await InvokeAsync("update", "--check", "-f", "-y");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Update);
        invocation.Options.UpdateCheck.ShouldBeTrue();
        invocation.Options.UpdateForce.ShouldBeTrue();
        invocation.Options.UpdateYes.ShouldBeTrue();
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
    public async Task CaptureJsonIsAvailableForCaptureAndTmux()
    {
        var capture = await InvokeAsync("capture", "--json", "--", "echo", "hello");
        var tmux = await InvokeAsync("tmux", "capture", "--target", "%1", "--json");

        capture.ExitCode.ShouldBe(0);
        capture.Options!.Json.ShouldBeTrue();
        tmux.ExitCode.ShouldBe(0);
        tmux.Options!.Workflow.ShouldBe(Workflow.Tmux);
        tmux.Options.Json.ShouldBeTrue();
    }

    [Test]
    public async Task SessionCommandsMapIdsAndInputs()
    {
        var start = await InvokeAsync(
            "session",
            "start",
            "--width",
            "90",
            "--height",
            "30",
            "--",
            "sh",
            "-c",
            "read value"
        );
        var read = await InvokeAsync("session", "read", "s_abc");
        var wait = await InvokeAsync(
            "session",
            "wait",
            "s_abc",
            "--text",
            "Working",
            "--until",
            "absent",
            "--stable-for",
            "2s",
            "--timeout",
            "3m"
        );
        var send = await InvokeAsync(
            "session",
            "send",
            "s_abc",
            "--text",
            "i",
            "--keys",
            "Enter",
            "--text",
            "hello",
            "--keys",
            "Esc"
        );
        var resize = await InvokeAsync(
            "session",
            "resize",
            "s_abc",
            "--width",
            "100",
            "--height",
            "40"
        );
        var capture = await InvokeAsync(
            "session",
            "capture",
            "s_abc",
            "--out",
            "screen.svg"
        );
        var list = await InvokeAsync("session", "list");
        var stop = await InvokeAsync("session", "stop", "--all", "--yes");

        start.ExitCode.ShouldBe(0);
        start.Options!.RequestedSessionAction.ShouldBe(SessionAction.Start);
        start.Options.SessionWidth.ShouldBe(90);
        start.Options.SessionHeight.ShouldBe(30);
        start.Options.SessionCommand.ShouldBe(["sh", "-c", "read value"]);
        read.ExitCode.ShouldBe(0);
        read.Options!.RequestedSessionAction.ShouldBe(SessionAction.Read);
        read.Options.SessionId.ShouldBe("s_abc");
        wait.ExitCode.ShouldBe(0);
        wait.Options!.RequestedSessionAction.ShouldBe(SessionAction.Wait);
        wait.Options.SessionId.ShouldBe("s_abc");
        wait.Options.SessionWaitText.ShouldBe("Working");
        wait.Options.SessionWaitUntil.ShouldBe("absent");
        wait.Options.SessionWaitStableFor.ShouldBe("2s");
        wait.Options.SessionWaitTimeout.ShouldBe("3m");
        send.ExitCode.ShouldBe(0);
        send.Options!.SessionInputs.ShouldBe(
            [
                new SessionInputStep(true, "i"),
                new SessionInputStep(false, "Enter"),
                new SessionInputStep(true, "hello"),
                new SessionInputStep(false, "Esc"),
            ]
        );
        resize.ExitCode.ShouldBe(0);
        resize.Options!.SessionWidth.ShouldBe(100);
        resize.Options.SessionHeight.ShouldBe(40);
        capture.ExitCode.ShouldBe(0);
        capture.Options!.RequestedSessionAction.ShouldBe(SessionAction.Capture);
        list.ExitCode.ShouldBe(0);
        list.Options!.RequestedSessionAction.ShouldBe(SessionAction.List);
        stop.ExitCode.ShouldBe(0);
        stop.Options!.SessionAll.ShouldBeTrue();
        stop.Options.SessionYes.ShouldBeTrue();
    }

    [Test]
    public async Task SessionCommandsDoNotAcceptJsonFlag()
    {
        var invocation = await InvokeAsync("session", "list", "--json");

        invocation.ExitCode.ShouldBe(1);
        invocation.Error.ShouldContain("--json");
    }

    [Test]
    public async Task SessionReadDoesNotAcceptRemovedWaitOption()
    {
        var invocation = await InvokeAsync("session", "read", "s_abc", "--wait", "1s");

        invocation.ExitCode.ShouldBe(1);
        invocation.Error.ShouldContain("--wait");
    }

    [Test]
    public async Task SessionSendRequiresAtLeastOneInputStep()
    {
        var missing = await InvokeAsync("session", "send", "s_abc");
        var repeated = await InvokeAsync(
            "session",
            "send",
            "s_abc",
            "--text",
            "hello",
            "--keys",
            "Enter",
            "--text",
            "again"
        );

        missing.ExitCode.ShouldBe(1);
        missing.Error.ShouldContain("at least one --keys, --text, --paste, or --raw-hex");
        repeated.ExitCode.ShouldBe(0);
        repeated.Options!.SessionInputs.ShouldBe(
            [
                new SessionInputStep(true, "hello"),
                new SessionInputStep(false, "Enter"),
                new SessionInputStep(true, "again"),
            ]
        );
    }

    [Test]
    public async Task InteractiveAcceptsSaveCastForDebug()
    {
        var invocation = await InvokeAsync(
            "interactive",
            "--save-cast",
            "debug.cast",
            "--",
            "echo",
            "hi"
        );

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.Interactive);
        invocation.Options.SaveCastPath.ShouldBe("debug.cast");
    }

    [Test]
    public async Task LiveServerAcceptsSaveCastForDebug()
    {
        var invocation = await InvokeAsync("live-server", "--save-cast", "debug.cast");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Workflow.ShouldBe(Workflow.LiveServer);
        invocation.Options.SaveCastPath.ShouldBe("debug.cast");

        var tmux = await InvokeAsync(
            "tmux",
            "live-server",
            "--target",
            ":0",
            "--save-cast",
            "debug.cast"
        );

        tmux.ExitCode.ShouldBe(0);
        tmux.Options!.Workflow.ShouldBe(Workflow.Tmux);
        tmux.Options.RequestedTmuxAction.ShouldBe(TmuxAction.LiveServer);
        tmux.Options.SaveCastPath.ShouldBe("debug.cast");
    }

    [Test]
    public async Task MouseIsOnlyAvailableWithInteractiveOrLiveServer()
    {
        var interactive = await InvokeAsync("interactive", "--mouse", "true", "--", "vim");
        interactive.ExitCode.ShouldBe(0);
        interactive.Options!.Mouse.ShouldBeTrue();

        var liveServer = await InvokeAsync("live-server", "--mouse", "false");
        liveServer.ExitCode.ShouldBe(0);
        liveServer.Options!.Mouse.ShouldBeFalse();

        var capture = await InvokeAsync("capture", "--mouse", "true", "--", "echo");
        capture.ExitCode.ShouldBe(1);
        capture.Options.ShouldBeNull();
        capture.Error.ShouldContain("--mouse is only available with interactive/live-server.");
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

        // --listen was removed in v0.10; the positional host:port is the only way
        // to set the listen address. A stray --listen token is treated as a
        // command (consistent with other unknown options) and must not
        // configure the listener.
        var removedListenOption = await InvokeAsync("live-server", "--listen", "127.0.0.1");
        removedListenOption.Options.ShouldNotBeNull();
        removedListenOption.Options!.ListenAddress.ShouldBeNull();
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
    public async Task ThemeOptionAcceptsShortAlias()
    {
        var invocation = await InvokeAsync("capture", "-t", "cyberpunk-pc", "--", "echo");

        invocation.ExitCode.ShouldBe(0);
        invocation.Options!.Themes.ShouldBe(["cyberpunk-pc"]);
    }

    [Test]
    public async Task GeneratedHelpKeepsSvgConverterColumnCompact()
    {
        var invocation = await InvokeAsync("capture", "--help");

        invocation.ExitCode.ShouldBe(0);
        invocation.Output.ShouldContain("--svg-converter");
        invocation.Output.ShouldContain("<converter>");
        invocation.Output.ShouldNotContain("--svg-converter <auto|ffmpeg|resvg|rsvg|rsvg-convert>");
        invocation.Output.ShouldNotContain("<command>");
    }

    [Test]
    public async Task GeneratedHelpHidesReplayInputPlaceholder()
    {
        var invocation = await InvokeAsync("replay", "--help");

        invocation.ExitCode.ShouldBe(0);
        invocation.Output.ShouldNotContain("<replay.json>");
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
        zsh.Output.ShouldContain(
            "--verbose=[Enable verbose logging; optionally write to a file (overwritten; defaults to a timestamped file).]: :_files"
        );
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

    private sealed record Invocation(
        int ExitCode,
        AppOptions? Options,
        string Output,
        string Error
    );
}
