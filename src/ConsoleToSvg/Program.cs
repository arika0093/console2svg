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
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(OptionParser.HelpText);
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

            Console.Error.WriteLine($"Generated: {options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
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
            return await PtyRecorder.RecordAsync(
                    options.Command!,
                    options.Width,
                    options.Height,
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

        return await PipeRecorder.RecordAsync(
                Console.OpenStandardInput(),
                options.Width,
                options.Height,
                cancellationToken
            )
            .ConfigureAwait(false);
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