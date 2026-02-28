using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parseResult = OptionParser.TryParse(
            args,
            out var options,
            out var error,
            out var showHelp
        );
        if (!parseResult)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(OptionParser.ShortHelpText);
            return 1;
        }

        if (showHelp || options is null)
        {
            Console.WriteLine(OptionParser.HelpText);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(ThisAssembly.AssemblyInformationalVersion);
            return 0;
        }

        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.WriteLine(OptionParser.ShortHelpText);
            return 0;
        }

        using var loggerFactory = CreateLoggerFactory(options.Verbose);
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.Program");
        logger.ZLogDebug(
            $"Starting console2svg. Verbose={options.Verbose} Args={string.Join(' ', args)}"
        );
        logger.ZLogDebug(
            $"Parsed options: Mode={options.Mode} Out={options.OutputPath} In={options.InputCastPath ?? ""} Command={options.Command ?? ""} Width={options.Width} Height={options.Height} Frame={options.Frame} Theme={options.Theme} Window={options.Window} Padding={options.Padding} SaveCast={options.SaveCastPath ?? ""} Font={options.Font ?? ""}"
        );

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
            logger.ZLogDebug($"Cancellation requested by Ctrl+C.");
        };

        if (options.Timeout.HasValue)
        {
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(options.Timeout.Value));
            logger.ZLogDebug($"Timeout set: {options.Timeout.Value} seconds.");
        }

        try
        {
            var session = await LoadOrRecordAsync(
                    options,
                    loggerFactory,
                    cancellationTokenSource.Token
                )
                .ConfigureAwait(false);
            var wasCanceled = cancellationTokenSource.IsCancellationRequested;
            var outputToken = wasCanceled ? CancellationToken.None : cancellationTokenSource.Token;
            logger.ZLogDebug(
                $"Recording loaded. Events={session.Events.Count} Width={session.Header.width} Height={session.Header.height}"
            );

            if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            {
                logger.ZLogDebug($"Saving asciicast to {options.SaveCastPath}");
                await AsciicastWriter
                    .WriteToFileAsync(options.SaveCastPath!, session, outputToken)
                    .ConfigureAwait(false);
                logger.ZLogDebug($"Saved asciicast to {options.SaveCastPath}");
            }

            var renderOptions = SvgRenderOptions.FromAppOptions(options);
            logger.ZLogDebug($"Rendering SVG. Mode={options.Mode}");
            var svg =
                options.Mode == OutputMode.Video
                    ? AnimatedSvgRenderer.Render(session, renderOptions)
                    : SvgRenderer.Render(session, renderOptions);
            logger.ZLogDebug($"Rendering completed. SvgLength={svg.Length}");

            EnsureDirectory(options.OutputPath);
            logger.ZLogDebug($"Writing output file: {options.OutputPath}");
            await File.WriteAllTextAsync(
                    options.OutputPath,
                    svg,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    outputToken
                )
                .ConfigureAwait(false);
            logger.ZLogDebug($"Output file written: {options.OutputPath}");

            if (wasCanceled)
            {
                await Console.Error.WriteLineAsync($"Generated (partial): {options.OutputPath}");
                return 0;
            }

            await Console.Error.WriteLineAsync($"Generated: {options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.ZLogDebug($"Execution canceled.");
            await Console.Error.WriteLineAsync("Canceled.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"Unhandled exception occurred: {ex.Message}");
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static async Task<RecordingSession> LoadOrRecordAsync(
        AppOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.LoadOrRecord");
        if (!string.IsNullOrWhiteSpace(options.InputCastPath))
        {
            logger.ZLogDebug($"Input source: asciicast file. Path={options.InputCastPath}");
            return await AsciicastReader
                .ReadFromFileAsync(options.InputCastPath!, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(options.Command))
        {
            var ptyWidth = options.Width ?? 80;
            var ptyHeight = options.Height ?? 24;
            logger.ZLogDebug(
                $"Input source: PTY command. Command={options.Command} Width={ptyWidth} Height={ptyHeight}"
            );
            return await PtyRecorder
                .RecordAsync(
                    options.Command!,
                    ptyWidth,
                    ptyHeight,
                    cancellationToken,
                    loggerFactory.CreateLogger("ConsoleToSvg.PtyRecorder"),
                    forwardToConsole: options.Mode == OutputMode.Video || !options.Verbose,
                    replaySavePath: options.ReplaySavePath,
                    replayPath: options.ReplayPath
                )
                .ConfigureAwait(false);
        }

        if (!Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "No input source. Use --command, --in, or pipe stdout into console2svg."
            );
        }

        var pipeWidth = options.Width ?? TryGetConsoleWidth() ?? 80;
        var pipeHeight = options.Height ?? TryGetConsoleHeight() ?? 24;
        logger.ZLogDebug($"Input source: stdin pipe. Width={pipeWidth} Height={pipeHeight}");
        return await PipeRecorder
            .RecordAsync(
                Console.OpenStandardInput(),
                pipeWidth,
                pipeHeight,
                cancellationToken,
                loggerFactory.CreateLogger("ConsoleToSvg.PipeRecorder")
            )
            .ConfigureAwait(false);
    }

    private static ILoggerFactory CreateLoggerFactory(bool verbose)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddZLoggerConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.None);
            if (verbose)
            {
                builder.AddZLoggerFile("console2svg.log");
            }
        });
    }

    private static int? TryGetConsoleWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            return w > 0 ? w : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetConsoleHeight()
    {
        try
        {
            var h = Console.WindowHeight;
            return h > 0 ? h : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
