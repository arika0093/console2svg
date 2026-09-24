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
    private Argument<string[]>? _captureCommandArgument;

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

    internal static bool TryParseCaptureOptions(
        IReadOnlyList<string> args,
        out AppOptions? options,
        out bool hasOutput,
        out string? error
    )
    {
        var commandLine = Create((_, _, _) => Task.FromResult(0));
        var parseResult = commandLine.Parse(["capture", .. args, "--", "__c2s_batch__"]);
        if (parseResult.Errors.Count > 0)
        {
            options = null;
            hasOutput = false;
            error = string.Join(" ", parseResult.Errors.Select(item => item.Message));
            return false;
        }

        hasOutput = parseResult.GetResult(commandLine._symbols.OutputPath) is { Implicit: false };

        if (
            !commandLine.TryCreateOptions(
                parseResult,
                Workflow.Capture,
                commandLine._captureCommandArgument,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                out options,
                out error
            )
        )
        {
            return false;
        }

        if (!string.Equals(options!.Command, "__c2s_batch__", StringComparison.Ordinal))
        {
            var unexpected = options.Command?[..^"__c2s_batch__".Length].TrimEnd();
            options = null;
            hasOutput = false;
            error = $"unsupported marker option or argument '{unexpected}'.";
            return false;
        }

        return true;
    }

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
        _captureCommandArgument = AddCommandArgument(capture);
        SetMappedAction(capture, Workflow.Capture, _captureCommandArgument);
        root.Subcommands.Add(capture);

        var interactive = new Command("interactive", "Capture an interactive shell or program.");
        AddOptions(interactive, _symbols.InteractiveOptions);
        SetMappedAction(interactive, Workflow.Interactive, AddCommandArgument(interactive));
        root.Subcommands.Add(interactive);

        var replay = new Command("replay", "Replay keyboard input while capturing a command.");
        var replayPath = new Argument<string>("replay.json")
        {
            Description = "Recorded keyboard input.",
            Hidden = true,
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

        var cast = new Command("cast", "Render an asciicast file.");
        var castPath = new Argument<string>("cast")
        {
            Description = "Asciicast v2 file.",
            Hidden = true,
        };
        cast.Arguments.Add(castPath);
        AddOptions(cast, _symbols.CastOptions);
        SetMappedAction(cast, Workflow.Cast, inputPath: castPath);
        root.Subcommands.Add(cast);

        AddThemeCommand(root);
        AddStatusCommand(root);
        AddUpdateCommand(root);
        AddLlmCommand(root);
        AddSessionCommand(root);
        AddLiveServerCommand(root);
        AddTmuxCommand(root);
        AddBatchCommand(root);
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

    private static Argument<string[]> AddCommandArgument(
        Command command,
        bool captureRemainingTokens = true
    )
    {
        var argument = new Argument<string[]>("command")
        {
            Arity = ArgumentArity.ZeroOrMore,
            CaptureRemainingTokens = captureRemainingTokens,
            Description = "Command to capture. Use -- to preserve argument boundaries.",
            Hidden = true,
        };
        command.Arguments.Add(argument);
        return argument;
    }

    private void AddThemeCommand(RootCommand root)
    {
        var theme = new Command("theme", "Manage installed themes.");
        theme.SetAction(ColoredHelpAction.Write);

        var list = new Command("list", "List installed themes.");
        list.Options.Add(_symbols.ThemeFormat);
        SetMappedAction(
            list,
            Workflow.Theme,
            themeAction: ThemeAction.List,
            formatOption: _symbols.ThemeFormat
        );
        theme.Subcommands.Add(list);

        var install = new Command("install", "Install a theme from a directory, archive, or URL.");
        var source = new Argument<string>("source")
        {
            Description = "Theme source.",
            Hidden = true,
        };
        install.Arguments.Add(source);
        SetMappedAction(
            install,
            Workflow.Theme,
            themeAction: ThemeAction.Install,
            themeArgument: source
        );
        theme.Subcommands.Add(install);

        var remove = new Command("remove", "Remove an installed theme.");
        var id = new Argument<string>("id") { Description = "Installed theme ID.", Hidden = true };
        remove.Arguments.Add(id);
        SetMappedAction(remove, Workflow.Theme, themeAction: ThemeAction.Remove, themeArgument: id);
        theme.Subcommands.Add(remove);

        var update = new Command("update", "Update one theme, or all installed themes.");
        var updateId = new Argument<string?>("id")
        {
            Description = "Installed theme ID.",
            Hidden = true,
        };
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
        var endpoint = new Argument<string?>("host:port")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Optional host and port to listen on (for example, 127.0.0.1:8080).",
            HelpName = "host:port",
            Hidden = true,
        };
        liveServer.Arguments.Add(endpoint);
        SetMappedAction(
            liveServer,
            Workflow.LiveServer,
            AddCommandArgument(liveServer, captureRemainingTokens: false),
            portArgument: endpoint
        );
        root.Subcommands.Add(liveServer);
    }

    private void AddStatusCommand(RootCommand root)
    {
        var status = new Command("status", "Show application and dependency status.");
        status.Options.Add(_symbols.StatusJson);
        status.Options.Add(_symbols.StatusFormat);
        status.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Status,
                        StatusJson = parseResult.GetValue(_symbols.StatusJson),
                        OutputFormat = parseResult.GetValue(_symbols.StatusJson)
                            ? OutputFormat.Json
                            : ParseOutputFormat(parseResult.GetValue(_symbols.StatusFormat)),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        root.Subcommands.Add(status);
    }

    private static OutputFormat ParseOutputFormat(string? value) =>
        Enum.TryParse<OutputFormat>(value, true, out var format) ? format : OutputFormat.Table;

    private static void AddLlmCommand(RootCommand root)
    {
        var llm = new Command("llm", "LLM integration helpers.");
        llm.SetAction(ColoredHelpAction.Write);

        var skills = new Command("skills", "Write the console2svg Agent Skill to standard output.");
        skills.SetAction(
            (parseResult, cancellationToken) =>
                LlmSkill.WriteAsync(
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
        llm.Subcommands.Add(skills);
        root.Subcommands.Add(llm);
    }

    private void AddUpdateCommand(RootCommand root)
    {
        var update = new Command("update", "Check for or install a newer console2svg version.");
        var check = new Option<bool>("--check")
        {
            Description = "Only check for an available update.",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Update even when a package manager owns this installation.",
        };
        force.Aliases.Add("-f");
        var yes = new Option<bool>("--yes") { Description = "Skip the confirmation prompt." };
        yes.Aliases.Add("-y");
        update.Options.Add(check);
        update.Options.Add(force);
        update.Options.Add(yes);
        update.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Update,
                        UpdateCheck = parseResult.GetValue(check),
                        UpdateForce = parseResult.GetValue(force),
                        UpdateYes = parseResult.GetValue(yes),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        root.Subcommands.Add(update);
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
        var endpoint = new Argument<string?>("host:port")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Optional host and port to listen on (for example, 127.0.0.1:8080).",
            HelpName = "host:port",
            Hidden = true,
        };
        liveServer.Arguments.Add(endpoint);
        SetMappedAction(
            liveServer,
            Workflow.Tmux,
            tmuxAction: TmuxAction.LiveServer,
            portArgument: endpoint
        );
        tmux.Subcommands.Add(liveServer);

        root.Subcommands.Add(tmux);
    }

    private void AddSessionCommand(RootCommand root)
    {
        var session = new Command("session", "Manage LLM-controlled terminal sessions.");
        session.SetAction(ColoredHelpAction.Write);

        var start = new Command("start", "Start a command in a managed PTY session.");
        AddOptions(
            start,
            [
                _symbols.SessionWidth,
                _symbols.SessionHeight,
                _symbols.SessionWorkingDirectory,
                _symbols.NoDeleteEnvs,
            ]
        );
        var commandArgument = AddCommandArgument(start);
        start.SetAction(
            (parseResult, cancellationToken) =>
            {
                var command = parseResult.GetValue(commandArgument) ?? [];
                if (
                    command.Length == 0
                    || !parseResult.Tokens.Any(token => token.Type == TokenType.DoubleDash)
                )
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(
                        "A command must be specified after --."
                    );
                    return Task.FromResult(1);
                }

                return _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Start,
                        SessionWidth = parseResult.GetValue(_symbols.SessionWidth) ?? 100,
                        SessionHeight = parseResult.GetValue(_symbols.SessionHeight) ?? 24,
                        SessionCommand = command,
                        SessionWorkingDirectory =
                            parseResult.GetValue(_symbols.SessionWorkingDirectory)
                            ?? Environment.CurrentDirectory,
                        NoDeleteEnvs = parseResult.GetValue(_symbols.NoDeleteEnvs),
                    },
                    parseResult,
                    cancellationToken
                );
            }
        );
        session.Subcommands.Add(start);

        var list = new Command("list", "List managed sessions.");
        list.Options.Add(_symbols.SessionListAll);
        list.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.List,
                        SessionListAll = parseResult.GetValue(_symbols.SessionListAll),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        session.Subcommands.Add(list);

        var read = new Command("read", "Read a managed session's current screen.");
        var readId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        read.Arguments.Add(readId);
        read.Options.Add(_symbols.SessionStructured);
        read.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Read,
                        SessionId = parseResult.GetRequiredValue(readId),
                        SessionStructured = parseResult.GetValue(_symbols.SessionStructured),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        session.Subcommands.Add(read);

        var wait = new Command("wait", "Wait for literal text in a managed session screen.");
        var waitId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        wait.Arguments.Add(waitId);
        AddOptions(
            wait,
            [
                _symbols.SessionWaitText,
                _symbols.SessionWaitUntil,
                _symbols.SessionWaitStableFor,
                _symbols.SessionWaitTimeout,
            ]
        );
        wait.SetAction(
            (parseResult, cancellationToken) =>
            {
                var text = parseResult.GetValue(_symbols.SessionWaitText);
                if (text is null)
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(
                        "Specify the literal text to wait for with --text."
                    );
                    return Task.FromResult(1);
                }

                return _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Wait,
                        SessionId = parseResult.GetRequiredValue(waitId),
                        SessionWaitText = text,
                        SessionWaitUntil = parseResult.GetValue(_symbols.SessionWaitUntil),
                        SessionWaitStableFor = parseResult.GetValue(_symbols.SessionWaitStableFor),
                        SessionWaitTimeout = parseResult.GetValue(_symbols.SessionWaitTimeout),
                    },
                    parseResult,
                    cancellationToken
                );
            }
        );
        session.Subcommands.Add(wait);

        var send = new Command(
            "send",
            "Send ordered text, paste content, semantic keys, or raw bytes to a session."
        );
        var sendId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        send.Arguments.Add(sendId);
        AddOptions(
            send,
            [
                _symbols.SessionKeys,
                _symbols.SessionText,
                _symbols.SessionPaste,
                _symbols.SessionRawHex,
            ]
        );
        send.SetAction(
            (parseResult, cancellationToken) =>
            {
                var inputs = ParseSessionInputs(parseResult);
                if (inputs.Count == 0)
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(
                        "Specify at least one --keys, --text, --paste, or --raw-hex input."
                    );
                    return Task.FromResult(1);
                }
                var options = new AppOptions
                {
                    Workflow = Workflow.Session,
                    RequestedSessionAction = SessionAction.Send,
                    SessionId = parseResult.GetRequiredValue(sendId),
                };
                options.SessionInputs.AddRange(inputs);
                return _handler(options, parseResult, cancellationToken);
            }
        );
        session.Subcommands.Add(send);

        var resize = new Command("resize", "Resize a managed session's terminal.");
        var resizeId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        resize.Arguments.Add(resizeId);
        AddOptions(resize, [_symbols.SessionWidth, _symbols.SessionHeight]);
        resize.SetAction(
            (parseResult, cancellationToken) =>
            {
                var width = parseResult.GetValue(_symbols.SessionWidth);
                var height = parseResult.GetValue(_symbols.SessionHeight);
                if (!width.HasValue || !height.HasValue)
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(
                        "Both --width and --height are required."
                    );
                    return Task.FromResult(1);
                }
                return _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Resize,
                        SessionId = parseResult.GetRequiredValue(resizeId),
                        SessionWidth = width.Value,
                        SessionHeight = height.Value,
                    },
                    parseResult,
                    cancellationToken
                );
            }
        );
        session.Subcommands.Add(resize);

        var capture = new Command("capture", "Render a managed session's current screen as SVG.");
        var captureId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        capture.Arguments.Add(captureId);
        AddOptions(capture, _symbols.SessionCaptureOptions);
        capture.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (
                    !TryCreateSessionCaptureOptions(
                        parseResult,
                        parseResult.GetRequiredValue(captureId),
                        out var options,
                        out var error
                    )
                )
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(error);
                    return 1;
                }
                return await _handler(options!, parseResult, cancellationToken)
                    .ConfigureAwait(false);
            }
        );
        session.Subcommands.Add(capture);

        var inspect = new Command(
            "inspect",
            "Render a managed session's current screen to an ephemeral SVG for visual inspection."
        );
        var inspectId = new Argument<string>("id")
        {
            Description = "Managed session ID.",
            Hidden = true,
        };
        inspect.Arguments.Add(inspectId);
        AddOptions(inspect, _symbols.SessionInspectOptions);
        inspect.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (
                    !TryCreateSessionInspectOptions(
                        parseResult,
                        parseResult.GetRequiredValue(inspectId),
                        out var options,
                        out var error
                    )
                )
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(error);
                    return 1;
                }
                return await _handler(options!, parseResult, cancellationToken)
                    .ConfigureAwait(false);
            }
        );
        session.Subcommands.Add(inspect);

        var stop = new Command("stop", "Stop a managed session or all managed sessions.");
        var stopId = new Argument<string?>("id")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Managed session ID.",
            Hidden = true,
        };
        stop.Arguments.Add(stopId);
        AddOptions(stop, [_symbols.SessionAll, _symbols.SessionYes]);
        stop.SetAction(
            (parseResult, cancellationToken) =>
            {
                var id = parseResult.GetValue(stopId);
                var all = parseResult.GetValue(_symbols.SessionAll);
                if (all == (id is not null))
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(
                        "Specify either a session ID or --all."
                    );
                    return Task.FromResult(1);
                }
                return _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Stop,
                        SessionId = id,
                        SessionAll = all,
                        SessionYes = parseResult.GetValue(_symbols.SessionYes),
                    },
                    parseResult,
                    cancellationToken
                );
            }
        );
        session.Subcommands.Add(stop);

        var host = new Command("host", "Internal managed-session worker.") { Hidden = true };
        host.Options.Add(_symbols.SessionPipeName);
        host.Options.Add(_symbols.SessionDirectory);
        host.SetAction(
            (parseResult, cancellationToken) =>
                _handler(
                    new AppOptions
                    {
                        Workflow = Workflow.Session,
                        RequestedSessionAction = SessionAction.Host,
                        SessionPipeName = parseResult.GetValue(_symbols.SessionPipeName),
                        SessionDirectory = parseResult.GetValue(_symbols.SessionDirectory),
                    },
                    parseResult,
                    cancellationToken
                )
        );
        session.Subcommands.Add(host);
        root.Subcommands.Add(session);
    }

    private static List<SessionInputStep> ParseSessionInputs(ParseResult parseResult)
    {
        var inputs = new List<SessionInputStep>();
        var tokens = parseResult.Tokens;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index].Value;
            var isText = token == "--text" || token.StartsWith("--text=", StringComparison.Ordinal);
            var isKey = token == "--keys" || token.StartsWith("--keys=", StringComparison.Ordinal);
            var isPaste =
                token == "--paste" || token.StartsWith("--paste=", StringComparison.Ordinal);
            var isRaw =
                token == "--raw-hex" || token.StartsWith("--raw-hex=", StringComparison.Ordinal);
            if (!isText && !isKey && !isPaste && !isRaw)
            {
                continue;
            }

            var equalsIndex = token.IndexOf('=');
            var value = equalsIndex >= 0 ? token[(equalsIndex + 1)..] : tokens[++index].Value;
            inputs.Add(new SessionInputStep(isText, value, isRaw, isPaste));
        }
        return inputs;
    }

    private bool TryCreateSessionCaptureOptions(
        ParseResult result,
        string sessionId,
        out AppOptions? options,
        out string? error
    )
    {
        options = new AppOptions
        {
            Workflow = Workflow.Session,
            RequestedSessionAction = SessionAction.Capture,
            SessionId = sessionId,
            OutputPath = result.GetValue(_symbols.OutputPath)?.ToString() ?? "output.svg",
            Format = IsSpecified(result, _symbols.Format)
                ? OutputFormats.Normalize(result.GetValue(_symbols.Format)!)
                : null,
            IsFormatExplicit = IsSpecified(result, _symbols.Format),
        };
        if (options.Format is not null)
        {
            options.OutputPath = OutputFormats.ApplyFormat(options.OutputPath, options.Format);
        }
        return ApplySessionAppearanceOptions(result, options, out error);
    }

    private bool TryCreateSessionInspectOptions(
        ParseResult result,
        string sessionId,
        out AppOptions? options,
        out string? error
    )
    {
        options = new AppOptions
        {
            Workflow = Workflow.Session,
            RequestedSessionAction = SessionAction.Inspect,
            SessionId = sessionId,
            OutputPath = string.Empty,
        };
        return ApplySessionAppearanceOptions(result, options, out error);
    }

    private bool ApplySessionAppearanceOptions(
        ParseResult result,
        AppOptions options,
        out string? error
    )
    {
        options.Font = result.GetValue(_symbols.Font);
        options.ForeColor = result.GetValue(_symbols.ForeColor);
        options.BackColor = result.GetValue(_symbols.BackColor);
        options.CropTop = result.GetValue(_symbols.CropTop) ?? "0";
        options.CropRight = result.GetValue(_symbols.CropRight) ?? "0";
        options.CropBottom = result.GetValue(_symbols.CropBottom) ?? "0";
        options.CropLeft = result.GetValue(_symbols.CropLeft) ?? "0";
        options.Opacity = result.GetValue(_symbols.Opacity) ?? 1d;
        options.Margin = result.GetValue(_symbols.Margin);
        options.Padding = result.GetValue(_symbols.Padding);
        options.FontSize = result.GetValue(_symbols.FontSize);
        options.PcPadding = result.GetValue(_symbols.PcPadding);
        options.LengthAdjust = result.GetValue(_symbols.Adjust) ?? "spacing";
        options.MaskAuto = result.GetValue(_symbols.MaskAuto);
        options.IsMaskAutoExplicit = IsSpecified(result, _symbols.MaskAuto);
        if (IsSpecified(result, _symbols.Window))
        {
            options.Window = result.GetValue(_symbols.Window) ?? "macos";
            options.IsWindowExplicit = true;
        }
        options.IsForeColorExplicit = IsSpecified(result, _symbols.ForeColor);
        options.IsBackColorExplicit = IsSpecified(result, _symbols.BackColor);
        options.IsFontExplicit = IsSpecified(result, _symbols.Font);
        options.IsFontSizeExplicit = IsSpecified(result, _symbols.FontSize);
        options.IsMarginExplicit = IsSpecified(result, _symbols.Margin);
        options.IsPaddingExplicit = IsSpecified(result, _symbols.Padding);
        options.IsOpacityExplicit = IsSpecified(result, _symbols.Opacity);
        options.Themes.AddRange(result.GetValue(_symbols.Theme) ?? []);
        options.MaskPatterns.AddRange(result.GetValue(_symbols.Mask) ?? []);
        ApplySize(result.GetValue(_symbols.Size), options);
        if (
            !ApplyBackground(
                result.GetValue(_symbols.Background)?.Select(value => value.ToString()).ToArray(),
                options,
                out error
            )
        )
        {
            return false;
        }
        error = null;
        return true;
    }

    private void AddBatchCommand(RootCommand root)
    {
        var batch = new Command("batch", "Run multiple captures in bulk.");
        batch.SetAction(ColoredHelpAction.Write);

        var markdown = new Command(
            "markdown",
            "Generate terminal images declared by c2s markers in Markdown files and update their image links. "
                + "Marker scripts execute shell commands."
        );
        AddOptions(markdown, _symbols.BatchMarkdownOptions);
        markdown.SetAction(
            async (parseResult, cancellationToken) =>
            {
                if (
                    !TryCreateOptions(
                        parseResult,
                        Workflow.Batch,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        out var options,
                        out var error
                    )
                )
                {
                    parseResult.InvocationConfiguration.Error.WriteLine(error);
                    parseResult.InvocationConfiguration.Error.WriteLine();
                    ColoredHelpAction.Write(parseResult);
                    return 1;
                }

                options!.BatchInputPath = parseResult.GetValue(_symbols.BatchInput) ?? "docs";
                options.BatchOutputDir = parseResult.GetValue(_symbols.BatchOutput) ?? "assets";
                options.BatchLinkBase = parseResult.GetValue(_symbols.BatchLinkBase);
                options.BatchFilters = parseResult.GetValue(_symbols.BatchFilter) ?? [];
                options.BatchDryRun = parseResult.GetValue(_symbols.BatchDryRun);
                options.BatchPlaceholder = parseResult.GetValue(_symbols.BatchPlaceholder);
                return await _handler(options, parseResult, cancellationToken)
                    .ConfigureAwait(false);
            }
        );
        batch.Subcommands.Add(markdown);

        var restore = new Command(
            "restore",
            "Restore generated assets from a local or remote manifest."
        );
        AddOptions(restore, _symbols.BatchRestoreOptions);
        var source = new Argument<string>("source")
        {
            Description = "Manifest URL, local manifest/directory, or Git repository source.",
            Hidden = true,
        };
        restore.Arguments.Add(source);
        restore.SetAction(
            (parseResult, cancellationToken) =>
            {
                var options = new AppOptions
                {
                    Workflow = Workflow.Batch,
                    RequestedBatchAction = BatchAction.Restore,
                    BatchInputPath = parseResult.GetRequiredValue(source),
                    BatchOutputDir = parseResult.GetValue(_symbols.BatchRestoreOutput),
                    BatchFilters = parseResult.GetValue(_symbols.BatchRestoreFilter) ?? [],
                    BatchDryRun = parseResult.GetValue(_symbols.BatchRestoreDryRun),
                    BatchForce = parseResult.GetValue(_symbols.BatchForce),
                    BatchPrune = parseResult.GetValue(_symbols.BatchPrune),
                };
                return _handler(options, parseResult, cancellationToken);
            }
        );
        batch.Subcommands.Add(restore);

        root.Subcommands.Add(batch);
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
