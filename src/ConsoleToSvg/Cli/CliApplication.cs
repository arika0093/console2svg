using System;
using System.Linq;
using System.Threading.Tasks;
using ConsoleAppFramework;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleApp.Version = ThisAssembly.AssemblyInformationalVersion;
        ConsoleApp.Log = message =>
        {
            if (!message.StartsWith("Usage:", StringComparison.Ordinal))
            {
                Console.WriteLine(message);
                return;
            }

            CliHelpFormatter.Write(
                $"console2svg [Ver: {ThisAssembly.AssemblyInformationalVersion}]"
                    + Environment.NewLine
                    + Environment.NewLine
                    + message
            );
        };
        ConsoleApp.LogError = Console.Error.WriteLine;

        var app = ConsoleApp.Create();
        app.Add<CliCommands>();

        Environment.ExitCode = 0;
        await app.RunAsync(NormalizeArguments(args)).ConfigureAwait(false);
        return Environment.ExitCode;
    }

    internal static async Task<int> ExecuteAsync(
        AppOptions options,
        ConsoleAppContext context
    )
    {
        if (!AppOptionsValidator.TryFinalize(options, out var error))
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
            return 1;
        }

        return await Program
            .RunOptionsAsync(options, context.Arguments.ToArray())
            .ConfigureAwait(false);
    }

    private static string[] NormalizeArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return Console.IsInputRedirected ? ["capture"] : [];
        }

        if (IsRootFrameworkOption(args))
        {
            return args;
        }

        if (IsVerb(args[0]))
        {
            var normalized = (string[])args.Clone();
            normalized[0] = normalized[0].ToLowerInvariant();
            return normalized;
        }

        return LegacyArgumentAdapter.Normalize(args);
    }

    private static bool IsRootFrameworkOption(string[] args) =>
        args.Length == 1
        && (
            args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("--version", StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsVerb(string value) =>
        Enum.TryParse<CliVerb>(value, ignoreCase: true, out _);
}

internal sealed class CliCommands
{
    /// <summary>Capture command output or redirected standard input.</summary>
    /// <param name="format">Explicit output format such as svg, png, gif, mp4, or webm.</param>
    /// <param name="animation">-v|--video, Render the captured timeline as an animation.</param>
    /// <param name="out">-o, Output file path; the default follows --format or is output.svg.</param>
    /// <param name="mode">-m, Output mode: image, video, or repeat.</param>
    /// <param name="width">-w, Terminal width in columns or "adjust".</param>
    /// <param name="height">Terminal height in rows or "adjust".</param>
    /// <param name="frame">Static frame index.</param>
    /// <param name="time">A time in seconds or range such as 1.5-3.0.</param>
    /// <param name="cropTop">Top crop in px, ch, or text syntax.</param>
    /// <param name="cropRight">Right crop in px or ch.</param>
    /// <param name="cropBottom">Bottom crop in px, ch, or text syntax.</param>
    /// <param name="cropLeft">Left crop in px or ch.</param>
    /// <param name="theme">Color theme name.</param>
    /// <param name="forecolor">Foreground color override.</param>
    /// <param name="backcolor">Terminal background color override.</param>
    /// <param name="font">CSS font-family.</param>
    /// <param name="fontSize">--fontsize, Font size in pixels.</param>
    /// <param name="window">-d, Window chrome style or custom JSON path.</param>
    /// <param name="padding">Outer padding in pixels.</param>
    /// <param name="noLoop">Disable animation looping.</param>
    /// <param name="fps">Maximum animation frames per second.</param>
    /// <param name="sleep">Seconds to retain the final frame.</param>
    /// <param name="fadeout">Fade-out duration in seconds.</param>
    /// <param name="timing">Animation timing mode.</param>
    /// <param name="coalesceMs">Output event coalescing in milliseconds or "auto".</param>
    /// <param name="opacity">Background opacity from 0 to 1.</param>
    /// <param name="adjust">SVG text length adjustment mode.</param>
    /// <param name="background">One or two background colors, or an image path.</param>
    /// <param name="pcMode">--pcmode, Enable desktop window mode.</param>
    /// <param name="pcPadding">Desktop padding override.</param>
    /// <param name="saveFrames">Directory for rendered static SVG frames.</param>
    /// <param name="size">Output size as WIDTH, WIDTHxHEIGHT, WIDTHx*, or *xHEIGHT.</param>
    /// <param name="svgConverter">SVG converter: auto, resvg, ffmpeg, or rsvg-convert.</param>
    /// <param name="mask">Strings to mask in rendered output.</param>
    /// <param name="stdout">Write SVG to standard output.</param>
    /// <param name="timeout">Capture timeout in seconds.</param>
    /// <param name="noColorEnv">--no-colorenv, Disable PTY color environment overrides.</param>
    /// <param name="noDeleteEnvs">Keep CI environment variables in the child process.</param>
    /// <param name="saveCast">Also save the raw asciicast session.</param>
    /// <param name="replaySave">Save keyboard input for later replay.</param>
    /// <param name="embedCast">Embed the asciicast source in SVG metadata.</param>
    /// <param name="embedLogs">Embed diagnostic logs in SVG metadata.</param>
    /// <param name="embedReplay">Record and embed keyboard input in SVG metadata.</param>
    /// <param name="embedDebug">Enable all embed options.</param>
    /// <param name="withCommand">-c, Prepend the command line to the rendering.</param>
    /// <param name="prompt">Prompt used with --with-command.</param>
    /// <param name="header">Explicit command header text.</param>
    /// <param name="verbose">Enable diagnostic logging.</param>
    /// <param name="verboseLog">Diagnostic log file path.</param>
    /// <param name="context">Command context; arguments after -- are the command to capture.</param>
    [Command("capture")]
    public Task<int> Capture(
        string? format = null,
        bool animation = false,
        string? @out = null,
        OutputMode? mode = null,
        [TerminalDimensionParser] TerminalDimension width = default,
        [TerminalDimensionParser] TerminalDimension height = default,
        int? frame = null,
        [TimeSelectionParser] TimeSelection time = default,
        string cropTop = "0",
        string cropRight = "0",
        string cropBottom = "0",
        string cropLeft = "0",
        string theme = "dark",
        string? forecolor = null,
        string? backcolor = null,
        string? font = null,
        double? fontSize = null,
        string window = "none",
        double? padding = null,
        bool noLoop = false,
        double fps = 12,
        double sleep = 0,
        double fadeout = 0,
        VideoTimingMode timing = VideoTimingMode.Deterministic,
        [CoalesceWindowParser] CoalesceWindow coalesceMs = default,
        double opacity = 1,
        string adjust = "spacing",
        string[]? background = null,
        bool pcMode = false,
        double? pcPadding = null,
        string? saveFrames = null,
        [OutputSizeParser] OutputSize size = default,
        [SvgConverterModeParser] SvgConverterMode svgConverter = SvgConverterMode.Auto,
        string[]? mask = null,
        bool stdout = false,
        double? timeout = null,
        bool noColorEnv = false,
        bool noDeleteEnvs = false,
        string? saveCast = null,
        string? replaySave = null,
        bool embedCast = false,
        bool embedLogs = false,
        bool embedReplay = false,
        bool embedDebug = false,
        bool withCommand = false,
        string? prompt = null,
        string? header = null,
        bool verbose = false,
        string? verboseLog = null,
        ConsoleAppContext context = default!
    )
    {
        var options = CreateRenderedOptions(
            CliVerb.Capture,
            context,
            format,
            animation,
            @out,
            mode,
            width,
            height,
            frame,
            time,
            cropTop,
            cropRight,
            cropBottom,
            cropLeft,
            theme,
            forecolor,
            backcolor,
            font,
            fontSize,
            window,
            padding,
            noLoop,
            fps,
            sleep,
            fadeout,
            timing,
            opacity,
            adjust,
            background,
            pcMode,
            pcPadding,
            saveFrames,
            size,
            svgConverter,
            mask,
            stdout,
            verbose,
            verboseLog
        );
        AppOptionsFactory.ApplyCaptureOptions(
            options,
            timeout,
            coalesceMs,
            noColorEnv,
            noDeleteEnvs,
            saveCast,
            replaySave,
            embedCast,
            embedLogs,
            embedReplay,
            embedDebug,
            withCommand,
            prompt,
            header
        );
        return CliApplication.ExecuteAsync(options, context);
    }

    /// <summary>Record a raw terminal session as an asciicast file.</summary>
    /// <param name="output">Output asciicast path.</param>
    /// <param name="width">-w, Terminal width in columns or "adjust".</param>
    /// <param name="height">Terminal height in rows or "adjust".</param>
    /// <param name="timeout">Recording timeout in seconds.</param>
    /// <param name="coalesceMs">Output event coalescing in milliseconds or "auto".</param>
    /// <param name="noColorEnv">--no-colorenv, Disable PTY color environment overrides.</param>
    /// <param name="noDeleteEnvs">Keep CI environment variables in the child process.</param>
    /// <param name="verbose">Enable diagnostic logging.</param>
    /// <param name="verboseLog">Diagnostic log file path.</param>
    /// <param name="context">Command context; arguments after -- are the command to record.</param>
    [Command("record")]
    public Task<int> Record(
        [Argument] string output,
        [TerminalDimensionParser] TerminalDimension width = default,
        [TerminalDimensionParser] TerminalDimension height = default,
        double? timeout = null,
        [CoalesceWindowParser] CoalesceWindow coalesceMs = default,
        bool noColorEnv = false,
        bool noDeleteEnvs = false,
        bool verbose = false,
        string? verboseLog = null,
        ConsoleAppContext context = default!
    ) =>
        CliApplication.ExecuteAsync(
            AppOptionsFactory.CreateRecord(
                output,
                width,
                height,
                timeout,
                coalesceMs,
                noColorEnv,
                noDeleteEnvs,
                verbose,
                verboseLog,
                context.EscapedArguments.ToArray()
            ),
            context
        );

    /// <summary>Render a saved asciicast without running a command.</summary>
    /// <param name="input">Input asciicast path; omit it to read redirected standard input.</param>
    /// <param name="format">Explicit output format.</param>
    /// <param name="animation">-v|--video, Render the recorded timeline as an animation.</param>
    /// <param name="out">-o, Output file path.</param>
    /// <param name="mode">-m, Output mode: image or video.</param>
    /// <param name="width">-w, Terminal width in columns or "adjust".</param>
    /// <param name="height">Terminal height in rows or "adjust".</param>
    /// <param name="frame">Static frame index.</param>
    /// <param name="time">A time in seconds or range such as 1.5-3.0.</param>
    /// <param name="cropTop">Top crop in px, ch, or text syntax.</param>
    /// <param name="cropRight">Right crop in px or ch.</param>
    /// <param name="cropBottom">Bottom crop in px, ch, or text syntax.</param>
    /// <param name="cropLeft">Left crop in px or ch.</param>
    /// <param name="theme">Color theme name.</param>
    /// <param name="forecolor">Foreground color override.</param>
    /// <param name="backcolor">Terminal background color override.</param>
    /// <param name="font">CSS font-family.</param>
    /// <param name="fontSize">--fontsize, Font size in pixels.</param>
    /// <param name="window">-d, Window chrome style or custom JSON path.</param>
    /// <param name="padding">Outer padding in pixels.</param>
    /// <param name="noLoop">Disable animation looping.</param>
    /// <param name="fps">Maximum animation frames per second.</param>
    /// <param name="sleep">Seconds to retain the final frame.</param>
    /// <param name="fadeout">Fade-out duration in seconds.</param>
    /// <param name="timing">Animation timing mode.</param>
    /// <param name="opacity">Background opacity from 0 to 1.</param>
    /// <param name="adjust">SVG text length adjustment mode.</param>
    /// <param name="background">One or two background colors, or an image path.</param>
    /// <param name="pcMode">--pcmode, Enable desktop window mode.</param>
    /// <param name="pcPadding">Desktop padding override.</param>
    /// <param name="saveFrames">Directory for rendered static SVG frames.</param>
    /// <param name="size">Output size as WIDTH, WIDTHxHEIGHT, WIDTHx*, or *xHEIGHT.</param>
    /// <param name="svgConverter">SVG converter selection.</param>
    /// <param name="mask">Strings to mask in rendered output.</param>
    /// <param name="stdout">Write SVG to standard output.</param>
    /// <param name="embedCast">Embed the asciicast source in SVG metadata.</param>
    /// <param name="embedLogs">Embed diagnostic logs in SVG metadata.</param>
    /// <param name="header">Explicit command header text.</param>
    /// <param name="verbose">Enable diagnostic logging.</param>
    /// <param name="verboseLog">Diagnostic log file path.</param>
    /// <param name="context">Command context.</param>
    [Command("render")]
    public Task<int> Render(
        [Argument] string? input = null,
        string? format = null,
        bool animation = false,
        string? @out = null,
        OutputMode? mode = null,
        [TerminalDimensionParser] TerminalDimension width = default,
        [TerminalDimensionParser] TerminalDimension height = default,
        int? frame = null,
        [TimeSelectionParser] TimeSelection time = default,
        string cropTop = "0",
        string cropRight = "0",
        string cropBottom = "0",
        string cropLeft = "0",
        string theme = "dark",
        string? forecolor = null,
        string? backcolor = null,
        string? font = null,
        double? fontSize = null,
        string window = "none",
        double? padding = null,
        bool noLoop = false,
        double fps = 12,
        double sleep = 0,
        double fadeout = 0,
        VideoTimingMode timing = VideoTimingMode.Deterministic,
        double opacity = 1,
        string adjust = "spacing",
        string[]? background = null,
        bool pcMode = false,
        double? pcPadding = null,
        string? saveFrames = null,
        [OutputSizeParser] OutputSize size = default,
        [SvgConverterModeParser] SvgConverterMode svgConverter = SvgConverterMode.Auto,
        string[]? mask = null,
        bool stdout = false,
        bool embedCast = false,
        bool embedLogs = false,
        string? header = null,
        bool verbose = false,
        string? verboseLog = null,
        ConsoleAppContext context = default!
    )
    {
        var options = CreateRenderedOptions(
            CliVerb.Render,
            context,
            format,
            animation,
            @out,
            mode,
            width,
            height,
            frame,
            time,
            cropTop,
            cropRight,
            cropBottom,
            cropLeft,
            theme,
            forecolor,
            backcolor,
            font,
            fontSize,
            window,
            padding,
            noLoop,
            fps,
            sleep,
            fadeout,
            timing,
            opacity,
            adjust,
            background,
            pcMode,
            pcPadding,
            saveFrames,
            size,
            svgConverter,
            mask,
            stdout,
            verbose,
            verboseLog,
            input
        );
        options.EmbedCast = embedCast;
        options.EmbedLogs = embedLogs;
        options.Header = header;
        return CliApplication.ExecuteAsync(options, context);
    }

    /// <summary>Capture a command while injecting recorded input.</summary>
    /// <param name="replayFile">Input replay file.</param>
    /// <param name="format">Explicit output format.</param>
    /// <param name="animation">-v|--video, Render the captured timeline as an animation.</param>
    /// <param name="out">-o, Output file path.</param>
    /// <param name="mode">-m, Output mode: image or video.</param>
    /// <param name="width">-w, Terminal width in columns or "adjust".</param>
    /// <param name="height">Terminal height in rows or "adjust".</param>
    /// <param name="frame">Static frame index.</param>
    /// <param name="time">A time in seconds or range such as 1.5-3.0.</param>
    /// <param name="cropTop">Top crop in px, ch, or text syntax.</param>
    /// <param name="cropRight">Right crop in px or ch.</param>
    /// <param name="cropBottom">Bottom crop in px, ch, or text syntax.</param>
    /// <param name="cropLeft">Left crop in px or ch.</param>
    /// <param name="theme">Color theme name.</param>
    /// <param name="forecolor">Foreground color override.</param>
    /// <param name="backcolor">Terminal background color override.</param>
    /// <param name="font">CSS font-family.</param>
    /// <param name="fontSize">--fontsize, Font size in pixels.</param>
    /// <param name="window">-d, Window chrome style or custom JSON path.</param>
    /// <param name="padding">Outer padding in pixels.</param>
    /// <param name="noLoop">Disable animation looping.</param>
    /// <param name="fps">Maximum animation frames per second.</param>
    /// <param name="sleep">Seconds to retain the final frame.</param>
    /// <param name="fadeout">Fade-out duration in seconds.</param>
    /// <param name="timing">Animation timing mode.</param>
    /// <param name="coalesceMs">Output event coalescing in milliseconds or "auto".</param>
    /// <param name="opacity">Background opacity from 0 to 1.</param>
    /// <param name="adjust">SVG text length adjustment mode.</param>
    /// <param name="background">One or two background colors, or an image path.</param>
    /// <param name="pcMode">--pcmode, Enable desktop window mode.</param>
    /// <param name="pcPadding">Desktop padding override.</param>
    /// <param name="saveFrames">Directory for rendered static SVG frames.</param>
    /// <param name="size">Output size as WIDTH, WIDTHxHEIGHT, WIDTHx*, or *xHEIGHT.</param>
    /// <param name="svgConverter">SVG converter selection.</param>
    /// <param name="mask">Strings to mask in rendered output.</param>
    /// <param name="stdout">Write SVG to standard output.</param>
    /// <param name="timeout">Capture timeout in seconds.</param>
    /// <param name="noColorEnv">--no-colorenv, Disable PTY color environment overrides.</param>
    /// <param name="noDeleteEnvs">Keep CI environment variables in the child process.</param>
    /// <param name="saveCast">Also save the raw asciicast session.</param>
    /// <param name="embedCast">Embed the asciicast source in SVG metadata.</param>
    /// <param name="embedLogs">Embed diagnostic logs in SVG metadata.</param>
    /// <param name="withCommand">-c, Prepend the command line to the rendering.</param>
    /// <param name="prompt">Prompt used with --with-command.</param>
    /// <param name="header">Explicit command header text.</param>
    /// <param name="verbose">Enable diagnostic logging.</param>
    /// <param name="verboseLog">Diagnostic log file path.</param>
    /// <param name="context">Command context; arguments after -- are the command to replay.</param>
    [Command("replay")]
    public Task<int> Replay(
        [Argument] string replayFile,
        string? format = null,
        bool animation = false,
        string? @out = null,
        OutputMode? mode = null,
        [TerminalDimensionParser] TerminalDimension width = default,
        [TerminalDimensionParser] TerminalDimension height = default,
        int? frame = null,
        [TimeSelectionParser] TimeSelection time = default,
        string cropTop = "0",
        string cropRight = "0",
        string cropBottom = "0",
        string cropLeft = "0",
        string theme = "dark",
        string? forecolor = null,
        string? backcolor = null,
        string? font = null,
        double? fontSize = null,
        string window = "none",
        double? padding = null,
        bool noLoop = false,
        double fps = 12,
        double sleep = 0,
        double fadeout = 0,
        VideoTimingMode timing = VideoTimingMode.Deterministic,
        [CoalesceWindowParser] CoalesceWindow coalesceMs = default,
        double opacity = 1,
        string adjust = "spacing",
        string[]? background = null,
        bool pcMode = false,
        double? pcPadding = null,
        string? saveFrames = null,
        [OutputSizeParser] OutputSize size = default,
        [SvgConverterModeParser] SvgConverterMode svgConverter = SvgConverterMode.Auto,
        string[]? mask = null,
        bool stdout = false,
        double? timeout = null,
        bool noColorEnv = false,
        bool noDeleteEnvs = false,
        string? saveCast = null,
        bool embedCast = false,
        bool embedLogs = false,
        bool withCommand = false,
        string? prompt = null,
        string? header = null,
        bool verbose = false,
        string? verboseLog = null,
        ConsoleAppContext context = default!
    )
    {
        var options = CreateRenderedOptions(
            CliVerb.Replay,
            context,
            format,
            animation,
            @out,
            mode,
            width,
            height,
            frame,
            time,
            cropTop,
            cropRight,
            cropBottom,
            cropLeft,
            theme,
            forecolor,
            backcolor,
            font,
            fontSize,
            window,
            padding,
            noLoop,
            fps,
            sleep,
            fadeout,
            timing,
            opacity,
            adjust,
            background,
            pcMode,
            pcPadding,
            saveFrames,
            size,
            svgConverter,
            mask,
            stdout,
            verbose,
            verboseLog,
            replayPath: replayFile
        );
        AppOptionsFactory.ApplyCaptureOptions(
            options,
            timeout,
            coalesceMs,
            noColorEnv,
            noDeleteEnvs,
            saveCast,
            replaySave: null,
            embedCast,
            embedLogs,
            embedReplay: false,
            embedDebug: false,
            withCommand,
            prompt,
            header
        );
        return CliApplication.ExecuteAsync(options, context);
    }

    /// <summary>Start an interactive shell or program.</summary>
    /// <param name="format">Explicit capture output format.</param>
    /// <param name="animation">-v|--video, Prefer animation output for recordings.</param>
    /// <param name="out">-o, Capture output template.</param>
    /// <param name="mode">-m, Output mode: image or video.</param>
    /// <param name="width">-w, Terminal width in columns or "adjust".</param>
    /// <param name="height">Terminal height in rows or "adjust".</param>
    /// <param name="frame">Static frame index.</param>
    /// <param name="time">A time in seconds or range such as 1.5-3.0.</param>
    /// <param name="cropTop">Top crop in px, ch, or text syntax.</param>
    /// <param name="cropRight">Right crop in px or ch.</param>
    /// <param name="cropBottom">Bottom crop in px, ch, or text syntax.</param>
    /// <param name="cropLeft">Left crop in px or ch.</param>
    /// <param name="theme">Color theme name.</param>
    /// <param name="forecolor">Foreground color override.</param>
    /// <param name="backcolor">Terminal background color override.</param>
    /// <param name="font">CSS font-family.</param>
    /// <param name="fontSize">--fontsize, Font size in pixels.</param>
    /// <param name="window">-d, Window chrome style or custom JSON path.</param>
    /// <param name="padding">Outer padding in pixels.</param>
    /// <param name="noLoop">Disable animation looping.</param>
    /// <param name="fps">Maximum animation frames per second.</param>
    /// <param name="sleep">Seconds to retain the final frame.</param>
    /// <param name="fadeout">Fade-out duration in seconds.</param>
    /// <param name="timing">Animation timing mode.</param>
    /// <param name="opacity">Background opacity from 0 to 1.</param>
    /// <param name="adjust">SVG text length adjustment mode.</param>
    /// <param name="background">One or two background colors, or an image path.</param>
    /// <param name="pcMode">--pcmode, Enable desktop window mode.</param>
    /// <param name="pcPadding">Desktop padding override.</param>
    /// <param name="saveFrames">Directory for rendered static SVG frames.</param>
    /// <param name="size">Output size as WIDTH, WIDTHxHEIGHT, WIDTHx*, or *xHEIGHT.</param>
    /// <param name="svgConverter">SVG converter selection.</param>
    /// <param name="mask">Strings to mask in rendered output.</param>
    /// <param name="timeout">Shell timeout in seconds.</param>
    /// <param name="noColorEnv">--no-colorenv, Disable PTY color environment overrides.</param>
    /// <param name="noDeleteEnvs">Keep CI environment variables in the child process.</param>
    /// <param name="noSuffix">Do not append a timestamp to output names.</param>
    /// <param name="verbose">Enable diagnostic logging.</param>
    /// <param name="verboseLog">Diagnostic log file path.</param>
    /// <param name="context">Command context; arguments after -- are the shell program.</param>
    [Command("shell")]
    public Task<int> Shell(
        string? format = null,
        bool animation = false,
        string? @out = null,
        OutputMode? mode = null,
        [TerminalDimensionParser] TerminalDimension width = default,
        [TerminalDimensionParser] TerminalDimension height = default,
        int? frame = null,
        [TimeSelectionParser] TimeSelection time = default,
        string cropTop = "0",
        string cropRight = "0",
        string cropBottom = "0",
        string cropLeft = "0",
        string theme = "dark",
        string? forecolor = null,
        string? backcolor = null,
        string? font = null,
        double? fontSize = null,
        string window = "none",
        double? padding = null,
        bool noLoop = false,
        double fps = 12,
        double sleep = 0,
        double fadeout = 0,
        VideoTimingMode timing = VideoTimingMode.Deterministic,
        double opacity = 1,
        string adjust = "spacing",
        string[]? background = null,
        bool pcMode = false,
        double? pcPadding = null,
        string? saveFrames = null,
        [OutputSizeParser] OutputSize size = default,
        [SvgConverterModeParser] SvgConverterMode svgConverter = SvgConverterMode.Auto,
        string[]? mask = null,
        double? timeout = null,
        bool noColorEnv = false,
        bool noDeleteEnvs = false,
        bool noSuffix = false,
        bool verbose = false,
        string? verboseLog = null,
        ConsoleAppContext context = default!
    )
    {
        var options = CreateRenderedOptions(
            CliVerb.Shell,
            context,
            format,
            animation,
            @out,
            mode,
            width,
            height,
            frame,
            time,
            cropTop,
            cropRight,
            cropBottom,
            cropLeft,
            theme,
            forecolor,
            backcolor,
            font,
            fontSize,
            window,
            padding,
            noLoop,
            fps,
            sleep,
            fadeout,
            timing,
            opacity,
            adjust,
            background,
            pcMode,
            pcPadding,
            saveFrames,
            size,
            svgConverter,
            mask,
            stdout: false,
            verbose,
            verboseLog
        );
        AppOptionsFactory.ApplyShellOptions(
            options,
            timeout,
            noColorEnv,
            noDeleteEnvs,
            noSuffix
        );
        return CliApplication.ExecuteAsync(options, context);
    }

    /// <summary>Manage theme packs (reserved).</summary>
    /// <param name="arguments">Theme action and its positional arguments.</param>
    [Command("theme")]
    public Task<int> Theme([Argument] params string[] arguments) =>
        Program.RunOptionsAsync(
            new AppOptions
            {
                Verb = CliVerb.Theme,
                ThemeArguments = arguments,
            },
            arguments
        );

    /// <summary>Report runtime features, tools, formats, and codecs.</summary>
    [Command("check")]
    public Task<int> Check() =>
        Program.RunOptionsAsync(
            new AppOptions { Verb = CliVerb.Check },
            ["check"]
        );

    private static AppOptions CreateRenderedOptions(
        CliVerb verb,
        ConsoleAppContext context,
        string? format,
        bool animation,
        string? @out,
        OutputMode? mode,
        TerminalDimension width,
        TerminalDimension height,
        int? frame,
        TimeSelection time,
        string cropTop,
        string cropRight,
        string cropBottom,
        string cropLeft,
        string theme,
        string? forecolor,
        string? backcolor,
        string? font,
        double? fontSize,
        string window,
        double? padding,
        bool noLoop,
        double fps,
        double sleep,
        double fadeout,
        VideoTimingMode timing,
        double opacity,
        string adjust,
        string[]? background,
        bool pcMode,
        double? pcPadding,
        string? saveFrames,
        OutputSize size,
        SvgConverterMode svgConverter,
        string[]? mask,
        bool stdout,
        bool verbose,
        string? verboseLog,
        string? inputCastPath = null,
        string? replayPath = null
    ) =>
        AppOptionsFactory.CreateRendered(
            verb,
            new RenderCommandSettings
            {
                Format = format,
                Animation = animation,
                Out = @out,
                Mode = mode,
                Width = width,
                Height = height,
                Frame = frame,
                Time = time,
                CropTop = cropTop,
                CropRight = cropRight,
                CropBottom = cropBottom,
                CropLeft = cropLeft,
                Theme = theme,
                ForeColor = forecolor,
                BackColor = backcolor,
                Font = font,
                FontSize = fontSize,
                Window = window,
                Padding = padding,
                NoLoop = noLoop,
                Fps = fps,
                Sleep = sleep,
                Fadeout = fadeout,
                Timing = timing,
                Opacity = opacity,
                Adjust = adjust,
                Background = background,
                PcMode = pcMode,
                PcPadding = pcPadding,
                SaveFrames = saveFrames,
                Size = size,
                SvgConverter = svgConverter,
                Mask = mask,
                Stdout = stdout,
                Verbose = verbose,
                VerboseLog = verboseLog,
            },
            inputCastPath,
            replayPath,
            context.EscapedArguments.ToArray()
        );
}
