using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parseResult = OptionParser.TryParse(args, out var options, out var error, out var showHelp);
        if (!parseResult)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync(OptionParser.HelpText);
            return 1;
        }

        if (showHelp || options is null)
        {
            Console.WriteLine(OptionParser.HelpText);
            return 0;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            var session = await LoadOrRecordAsync(options, cancellationTokenSource.Token).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(options.SaveCastPath))
            {
                await AsciicastWriter.WriteToFileAsync(options.SaveCastPath!, session, cancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }

            var renderOptions = SvgRenderOptions.FromAppOptions(options);
            var svg = options.Mode == OutputMode.Video
                ? AnimatedSvgRenderer.Render(session, renderOptions)
                : SvgRenderer.Render(session, renderOptions);

            EnsureDirectory(options.OutputPath);
            await File.WriteAllTextAsync(
                    options.OutputPath,
                    svg,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationTokenSource.Token
                )
                .ConfigureAwait(false);

            await Console.Error.WriteLineAsync($"Generated: {options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Canceled.");
            return 130;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static async Task<RecordingSession> LoadOrRecordAsync(AppOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.InputCastPath))
        {
            return await AsciicastReader.ReadFromFileAsync(options.InputCastPath!, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(options.Command))
        {
            var ptyWidth = options.Width ?? 80;
            var ptyHeight = options.Height ?? 24;
            return await PtyRecorder.RecordAsync(
                    options.Command!,
                    ptyWidth,
                    ptyHeight,
                    cancellationToken
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
        return await PipeRecorder.RecordAsync(
                Console.OpenStandardInput(),
                pipeWidth,
                pipeHeight,
                cancellationToken
            )
            .ConfigureAwait(false);
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