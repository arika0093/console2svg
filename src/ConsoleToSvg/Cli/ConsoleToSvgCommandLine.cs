using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.CommandLine.StaticCompletions;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

/// <summary>Builds and maps the console2svg command line.</summary>
public sealed partial class ConsoleToSvgCommandLine
{
    private readonly Func<AppOptions, ParseResult, CancellationToken, Task<int>> _handler;
    private readonly Symbols _symbols;

    private ConsoleToSvgCommandLine(
        Func<AppOptions, ParseResult, CancellationToken, Task<int>> handler
    )
    {
        _handler = handler;
        _symbols = new Symbols();
        RootCommand = CreateRootCommand();
    }

    public RootCommand RootCommand { get; }

    /// <summary>Creates the console2svg command hierarchy.</summary>
    public static ConsoleToSvgCommandLine Create(
        Func<AppOptions, ParseResult, CancellationToken, Task<int>> handler
    ) => new(handler ?? throw new ArgumentNullException(nameof(handler)));

    /// <summary>Parses arguments after preserving console2svg's special option syntax.</summary>
    public ParseResult Parse(IReadOnlyList<string> args) =>
        RootCommand.Parse(NormalizeCompatibilitySyntax(args));

    /// <summary>Formats generated help without maintaining static help text.</summary>
    public static string FormatHelp(ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        parseResult.InvocationConfiguration.Output = writer;
        try
        {
            _ = new HelpAction().Invoke(parseResult);
            return writer.ToString();
        }
        finally
        {
            parseResult.InvocationConfiguration.Output = output;
        }
    }

    private RootCommand CreateRootCommand()
    {
        var root = new RootCommand("Convert terminal output to SVG.") { HelpName = "console2svg" };
        ConfigureBuiltInActions(root);
        root.SetAction(ColoredHelpAction.Write);

        var capture = new Command("capture", "Capture terminal output as SVG.");
        AddOptions(capture, _symbols.CaptureOptions);
        SetMappedAction(capture, Workflow.Capture, AddCommandArgument(capture));
        root.Subcommands.Add(capture);

        var interactive = new Command("interactive", "Capture an interactive shell or program.");
        AddOptions(interactive, _symbols.InteractiveOptions);
        SetMappedAction(interactive, Workflow.Interactive, AddCommandArgument(interactive));
        root.Subcommands.Add(interactive);

        var replay = new Command("replay", "Replay keyboard input while capturing a command.");
        var replayPath = new Argument<string>("replay.json")
        {
            Description = "Recorded keyboard input.",
        };
        replay.Arguments.Add(replayPath);
        AddOptions(replay, _symbols.ReplayOptions);
        SetMappedAction(
            replay,
            Workflow.Replay,
            AddCommandArgument(replay),
            replayPath: replayPath
        );
        root.Subcommands.Add(replay);

        var convert = new Command("convert", "Render an asciicast or convert an SVG.");
        var input = new Argument<string>("input") { Description = "An asciicast v2 file or SVG." };
        convert.Arguments.Add(input);
        AddOptions(convert, _symbols.ConvertOptions);
        SetMappedAction(convert, Workflow.Convert, inputPath: input);
        root.Subcommands.Add(convert);

        AddThemeCommand(root);
        AddStatusCommand(root);
        AddLiveServerCommand(root);
        AddTmuxCommand(root);
        AddCompletionsCommand(root);
        return root;
    }

    private static void ConfigureBuiltInActions(RootCommand root)
    {
        var help = root.Options.OfType<HelpOption>().Single();
        help.Aliases.Clear();
        help.Action = new ColoredHelpAction();
        root.Options.OfType<VersionOption>().Single().Recursive = true;
    }

    private static void AddOptions(Command command, IEnumerable<Option> options)
    {
        foreach (var option in options)
            command.Options.Add(option);
    }

    private static Argument<string[]> AddCommandArgument(Command command)
    {
        var argument = new Argument<string[]>("command")
        {
            Arity = ArgumentArity.ZeroOrMore,
            CaptureRemainingTokens = true,
            Description = "Command to capture. Use -- to preserve argument boundaries.",
        };
        command.Arguments.Add(argument);
        return argument;
    }

    private void AddThemeCommand(RootCommand root)
    {
        var theme = new Command("theme", "Manage installed themes.");
        theme.SetAction(ColoredHelpAction.Write);

        var list = new Command("list", "List installed themes.");
        SetMappedAction(list, Workflow.Theme, themeAction: ThemeAction.List);
        theme.Subcommands.Add(list);

        var install = new Command("install", "Install a theme from a directory, archive, or URL.");
        var source = new Argument<string>("source") { Description = "Theme source." };
        install.Arguments.Add(source);
        SetMappedAction(
            install,
            Workflow.Theme,
            themeAction: ThemeAction.Install,
            themeArgument: source
        );
        theme.Subcommands.Add(install);

        var remove = new Command("remove", "Remove an installed theme.");
        var id = new Argument<string>("id") { Description = "Installed theme ID." };
        remove.Arguments.Add(id);
        SetMappedAction(remove, Workflow.Theme, themeAction: ThemeAction.Remove, themeArgument: id);
        theme.Subcommands.Add(remove);

        var update = new Command("update", "Update one theme, or all installed themes.");
        var updateId = new Argument<string?>("id") { Description = "Installed theme ID." };
        update.Arguments.Add(updateId);
        SetMappedAction(
            update,
            Workflow.Theme,
            themeAction: ThemeAction.Update,
            optionalThemeArgument: updateId
        );
        theme.Subcommands.Add(update);

        root.Subcommands.Add(theme);
    }

    private void AddLiveServerCommand(RootCommand root)
    {
        var liveServer = new Command("live-server", "Serve a live terminal SVG.");
        AddOptions(liveServer, _symbols.LiveServerOptions);
        var port = new Argument<string?>("port")
        {
            Description = "Port to listen on.",
            HelpName = "port",
        };
        liveServer.Arguments.Add(port);
        SetMappedAction(
            liveServer,
            Workflow.LiveServer,
            AddCommandArgument(liveServer),
            portArgument: port
        );
        root.Subcommands.Add(liveServer);
    }

    private void AddStatusCommand(RootCommand root)
    {
        var status = new Command("status", "Show application and dependency status.");
        status.Options.Add(_symbols.StatusJson);
        status.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Status,
                        StatusJson = parseResult.GetValue(_symbols.StatusJson),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        root.Subcommands.Add(status);
    }

    private void AddTmuxCommand(RootCommand root)
    {
        var tmux = new Command("tmux", "Capture or serve a tmux pane.");
        tmux.SetAction(ColoredHelpAction.Write);

        var capture = new Command("capture", "Capture a tmux pane as SVG.");
        AddOptions(capture, _symbols.TmuxCaptureOptions);
        SetMappedAction(capture, Workflow.Tmux, tmuxAction: TmuxAction.Capture);
        tmux.Subcommands.Add(capture);

        var liveServer = new Command("live-server", "Serve a tmux pane as a live SVG.");
        AddOptions(liveServer, _symbols.TmuxLiveServerOptions);
        var port = new Argument<string?>("port")
        {
            Description = "Port to listen on.",
            HelpName = "port",
        };
        liveServer.Arguments.Add(port);
        SetMappedAction(
            liveServer,
            Workflow.Tmux,
            tmuxAction: TmuxAction.LiveServer,
            portArgument: port
        );
        tmux.Subcommands.Add(liveServer);

        root.Subcommands.Add(tmux);
    }

    private static void AddCompletionsCommand(RootCommand root)
    {
        var completions = new CompletionsCommandDefinition();
        CompletionsCommandParser.ConfigureCommand(completions);
        completions.GenerateScriptCommand.SetAction(parseResult =>
        {
            var shell = parseResult.GetRequiredValue(
                completions.GenerateScriptCommand.ShellArgument
            );
            var provider = CompletionsCommandParser.ShellProviders[shell];
            var script = provider.GenerateCompletions(parseResult.RootCommandResult.Command);
            parseResult.InvocationConfiguration.Output.Write(
                NormalizeGeneratedCompletionScript(shell, script)
            );
        });
        root.Subcommands.Add(completions);
    }

    private static string NormalizeGeneratedCompletionScript(string shell, string script)
    {
        script = script.Replace("\r\n", "\n");
        return shell switch
        {
            "bash" => script
                .Replace("--out ", "--out -o ")
                .Replace(
                    "case $prev in",
                    """
                    case $prev in
                        --out|-o|--background|--verbose)
                            COMPREPLY=( $(compgen -f -- "$cur") )
                            return
                        ;;
                    """
                ),
            "zsh" => script.Replace(
                "*--background=[Desktop background color or image. Can be specified twice for a gradient.]: : ",
                "*--background=[Desktop background color or image. Can be specified twice for a gradient.]: :_files"
            ),
            _ => script,
        };
    }
}
