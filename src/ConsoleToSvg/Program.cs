using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    private const int DefaultWidth = 100;
    private const int DefaultHeight = 24;

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

        if (string.IsNullOrWhiteSpace(options.Prompt))
        {
            options.Prompt = GetDefaultPrompt();
        }

        using var loggerFactory = CreateLoggerFactory(options.Verbose, options.VerboseLogPath);
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.Program");
        logger.ZLogDebug(
            $"Application started. Version={ThisAssembly.AssemblyInformationalVersion} OS={Environment.OSVersion.Platform} Arch={RuntimeInformation.ProcessArchitecture}"
        );
        logger.ZLogDebug(
            $"Verbose={options.Verbose} VerboseLogPath={options.VerboseLogPath ?? "(default)"} Args={string.Join(' ', args)}"
        );
        logger.ZLogDebug(
            $"Parsed options: Mode={options.Mode} Out={options.OutputPath} In={options.InputCastPath ?? ""} Command={options.Command ?? ""} Width={options.Width} Height={options.Height} Frame={options.Frame} Theme={options.Theme} ForeColor={options.ForeColor ?? ""} Window={options.Window} Padding={options.Padding} SaveCast={options.SaveCastPath ?? ""} Font={options.Font ?? ""} LengthAdjust={options.LengthAdjust} Prompt={options.Prompt} Header={options.Header ?? ""} NoColorEnv={options.NoColorEnv} NoDeleteEnvs={options.NoDeleteEnvs} VideoTiming={options.VideoTiming}"
        );
        using var environmentScope = ApplyProcessEnvironmentOverrides(options, logger);

        var canceledByCtrlC = false;
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            canceledByCtrlC = true;
            cancellationTokenSource.Cancel();
            logger.ZLogDebug($"Cancellation requested by Ctrl+C.");
        };

        if (options.Timeout.HasValue)
        {
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(options.Timeout.Value));
            logger.ZLogDebug($"Timeout set: {options.Timeout.Value} seconds.");
        }

        if (options.StdOut)
        {
            // Redirect Console.Out → stderr before recording so that any third-party library
            // debug messages written via Console.Write/WriteLine (e.g. Quick.PtyNet's
            // "Waiting on {pid}" / "Wait succeeded" from its ChildWatcherThreadProc)
            // are sent to stderr instead of polluting the SVG output pipe.
            var stderrWriter = new StreamWriter(
                Console.OpenStandardError(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true
            ) { AutoFlush = true };
            Console.SetOut(stderrWriter);
            logger.ZLogDebug($"Console.Out redirected to stderr for --stdout mode.");
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
                options.Mode is OutputMode.Video or OutputMode.Repeat
                    ? AnimatedSvgRenderer.Render(session, renderOptions)
                    : SvgRenderer.Render(session, renderOptions);
            logger.ZLogDebug($"Rendering completed. SvgLength={svg.Length}");

            if (options.StdOut)
            {
                logger.ZLogDebug($"Writing SVG to stdout.");
                await using var stdoutWriter = new StreamWriter(
                    Console.OpenStandardOutput(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
                await stdoutWriter.WriteAsync(svg).ConfigureAwait(false);
                logger.ZLogDebug($"SVG written to stdout.");
            }
            else
            {
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
            }

            if (wasCanceled)
            {
                var cause = GetCancellationCause(options, canceledByCtrlC);
                logger.ZLogDebug($"Recording stopped. Cause={cause}");
                await Console.Error.WriteLineAsync(
                    options.StdOut ? "Generated (partial): (stdout)" : $"Generated (partial): {options.OutputPath}"
                );
                return 0;
            }

            await Console.Error.WriteLineAsync(
                options.StdOut ? "Generated: (stdout)" : $"Generated: {options.OutputPath}"
            );
            return 0;
        }
        catch (OperationCanceledException)
        {
            var cause = GetCancellationCause(options, canceledByCtrlC);
            logger.ZLogDebug($"Execution canceled. Cause={cause}");
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
            var ptyWidth = ResolveSize(options.Width, options.WidthAdjust, TryGetConsoleWidth, DefaultWidth);
            var ptyHeight = ResolveSize(options.Height, options.HeightAdjust, TryGetConsoleHeight, DefaultHeight);

            if (options.Mode == OutputMode.Repeat)
            {
                logger.ZLogDebug(
                    $"Input source: repeat command. Command={options.Command} Width={ptyWidth} Height={ptyHeight} Fps={options.VideoFps}"
                );
                return await RepeatRecorder
                    .RecordAsync(
                        options.Command!,
                        ptyWidth,
                        ptyHeight,
                        options.VideoFps,
                        cancellationToken,
                        loggerFactory.CreateLogger("ConsoleToSvg.RepeatRecorder"),
                        noDeleteEnvs: options.NoDeleteEnvs
                    )
                    .ConfigureAwait(false);
            }
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
                    forwardToConsole: !options.StdOut,
                    noDeleteEnvs: options.NoDeleteEnvs,
                    replaySavePath: options.ReplaySavePath,
                    replayPath: options.ReplayPath
                )
                .ConfigureAwait(false);
        }

        if (!Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                """
                No input source specified.
                Usage: 
                  console2svg "your-command with args" [options]
                  console2svg [options] -- your-command with args 
                  your-command with args | console2svg [options]
                For more details, see --help.
                """
            );
        }

        var pipeWidth = options.Width ?? TryGetConsoleWidth() ?? DefaultWidth;
        var pipeHeight = options.Height ?? TryGetConsoleHeight() ?? DefaultHeight;
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

    private static string GetCancellationCause(AppOptions options, bool canceledByCtrlC)
    {
        if (canceledByCtrlC)
        {
            return "Ctrl+C";
        }

        if (options.Timeout.HasValue)
        {
            return $"timeout ({options.Timeout.Value}s)";
        }

        return "cancellation";
    }

    private static ILoggerFactory CreateLoggerFactory(bool verbose, string? logPath)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            if (verbose)
            {
                var path = string.IsNullOrWhiteSpace(logPath) ? "console2svg.log" : logPath;
                builder.AddZLoggerFile(
                    path,
                    options =>
                    {
                        options.FileShared = false;
                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter(
                                $"[{0:local}] ",
                                (in template, in info) => template.Format(info.Timestamp)
                            );
                        });
                    }
                );
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.None);
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

    private static int ResolveSize(
        int? explicitValue,
        bool adjust,
        Func<int?> detectTerminalSize,
        int defaultValue
    )
    {
        if (explicitValue.HasValue)
        {
            return explicitValue.Value;
        }

        return adjust ? detectTerminalSize() ?? defaultValue : defaultValue;
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string GetDefaultPrompt()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "$";
        }

        try
        {
            return GetEffectiveUserId() == 0 ? "#" : "$";
        }
        catch
        {
            return "$";
        }
    }

    private static uint GetEffectiveUserId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return 1;
        }

        return geteuid();
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static IDisposable ApplyProcessEnvironmentOverrides(AppOptions options, ILogger logger)
    {
        var scope = new EnvironmentVariableScope(logger);

        // Ensure DOTNET_EnableWriteXorExecute=0 is set to prevent potential issues with
        // memory protection on some platforms, especially when dynamic code is involved.
        scope.Set("DOTNET_EnableWriteXorExecute", "0");

        if (!string.IsNullOrWhiteSpace(options.Command) && !options.NoColorEnv)
        {
            logger.ZLogDebug($"Applying color-related environment overrides.");
            // Ensure color-capable settings even on CI runners where TERM is unset/dumb.
            scope.Set("TERM", "xterm-256color");
            scope.Set("COLORTERM", "truecolor");
            scope.Set("FORCE_COLOR", "3");
        }

        return scope;
    }

    private sealed class EnvironmentVariableScope(ILogger logger) : IDisposable
    {
        private readonly Dictionary<string, (bool Exists, string? Value)> _originalValues = new(
            StringComparer.Ordinal
        );
        private readonly List<string> _appliedKeys = [];
        private bool _disposed;

        public void Set(string key, string value) => Apply(key, value);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (var i = _appliedKeys.Count - 1; i >= 0; i--)
            {
                var key = _appliedKeys[i];
                var original = _originalValues[key];
                try
                {
                    Environment.SetEnvironmentVariable(
                        key,
                        original.Exists ? original.Value : null
                    );
                }
                catch (Exception ex)
                {
                    logger.ZLogDebug(ex, $"Failed to restore environment variable: {key}");
                }
            }

            _disposed = true;
            logger.ZLogDebug($"Restored temporary environment variable overrides.");
        }

        private void Apply(string key, string? value)
        {
            if (!_originalValues.ContainsKey(key))
            {
                var original = Environment.GetEnvironmentVariable(key);
                _originalValues[key] = (original is not null, original);
                _appliedKeys.Add(key);
            }

            try
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to update environment variable: {key}");
            }
        }
    }
}
