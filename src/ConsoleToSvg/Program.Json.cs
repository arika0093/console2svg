using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg;

internal sealed class CaptureJsonResult
{
    public int SchemaVersion { get; init; } = 1;
    public string Status { get; init; } = "completed";
    public int? ExitCode { get; init; }
    public double? DurationMs { get; init; }
    public CaptureJsonScreen Screen { get; init; } = new();
    public CaptureJsonArtifact? Artifact { get; init; }
    public IReadOnlyList<CaptureJsonFrame> Frames { get; init; } = [];
}

internal sealed class CaptureJsonScreen
{
    public int Width { get; init; }
    public int Height { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool Truncated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CursorRow { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CursorColumn { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CursorVisible { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAlternateScreen { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ScrollbackRows { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenBufferSnapshot? Structured { get; init; }
}

internal sealed class CaptureJsonArtifact
{
    public string Path { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
}

internal sealed class CaptureJsonFrame
{
    public double TimeMs { get; init; }
    public string Path { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CaptureJsonResult))]
internal sealed partial class CaptureJsonContext : JsonSerializerContext { }

internal static partial class Program
{
    private const int JsonScreenTextLimit = 200_000;
    private const int JsonPreviewFrameLimit = 12;

    private static CaptureJsonResult CreateCaptureJsonResult(
        AppOptions options,
        RecordingSession recording,
        SvgRenderOptions renderOptions,
        bool wasCanceled,
        string? frameDirectory,
        double frameFps,
        string? outputFormat
    )
    {
        var theme = renderOptions.TerminalTheme ?? Theme.Resolve(renderOptions.Theme);
        var emulator = new TerminalEmulator(recording.Header.width, recording.Header.height, theme);
        foreach (var item in recording.Events)
        {
            emulator.Process(item.Data);
        }

        var lines = new string[emulator.Buffer.Height];
        var lastNonEmptyLine = -1;
        for (var row = 0; row < emulator.Buffer.Height; row++)
        {
            var line = new StringBuilder(emulator.Buffer.Width);
            for (var col = 0; col < emulator.Buffer.Width; col++)
            {
                var cellText = emulator.Buffer.GetCell(row, col).Text;
                if (!string.IsNullOrEmpty(cellText))
                {
                    line.Append(cellText);
                }
            }

            lines[row] = line.ToString().TrimEnd();
            if (lines[row].Length > 0)
            {
                lastNonEmptyLine = row;
            }
        }

        var fullText =
            lastNonEmptyLine < 0
                ? string.Empty
                : string.Join('\n', lines.Take(lastNonEmptyLine + 1));
        var textLength = Math.Min(fullText.Length, JsonScreenTextLimit);
        if (textLength < fullText.Length && char.IsHighSurrogate(fullText[textLength - 1]))
        {
            textLength--;
        }
        var text = fullText[..textLength];
        var framePaths = frameDirectory is null
            ? []
            : Directory
                .EnumerateFiles(frameDirectory, "frame-*.svg")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        var frames =
            framePaths.Length <= JsonPreviewFrameLimit
                ? framePaths.Select(path => CreateFrameReference(path, frameFps)).ToArray()
                : Enumerable
                    .Range(0, JsonPreviewFrameLimit)
                    .Select(index =>
                        CreateFrameReference(
                            framePaths[
                                (int)
                                    Math.Round(
                                        index
                                            * (framePaths.Length - 1d)
                                            / (JsonPreviewFrameLimit - 1d)
                                    )
                            ],
                            frameFps
                        )
                    )
                    .ToArray();

        return new CaptureJsonResult
        {
            Status = wasCanceled ? "partial" : "completed",
            ExitCode = recording.ExitCode,
            DurationMs = recording.DurationSeconds is double duration ? duration * 1000d : null,
            Screen = new CaptureJsonScreen
            {
                Width = emulator.Buffer.Width,
                Height = emulator.Buffer.Height,
                Text = text,
                Truncated = text.Length < fullText.Length,
            },
            Artifact = string.IsNullOrWhiteSpace(outputFormat)
                ? null
                : new CaptureJsonArtifact
                {
                    Path = Path.GetFullPath(options.OutputPath),
                    Format = outputFormat,
                },
            Frames = frames,
        };
    }

    private static CaptureJsonFrame CreateFrameReference(string path, double fps)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var frameIndex =
            name.StartsWith("frame-", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan("frame-".Length), out var parsedIndex)
                ? parsedIndex
                : 0;
        return new CaptureJsonFrame
        {
            TimeMs = frameIndex * 1000d / fps,
            Path = Path.GetFullPath(path),
        };
    }

    private static async Task WriteCaptureJsonAsync(
        CaptureJsonResult result,
        CancellationToken cancellationToken
    )
    {
        var json = JsonSerializer.Serialize(result, CaptureJsonContext.Default.CaptureJsonResult);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        await Console
            .OpenStandardOutput()
            .WriteAsync(bytes, cancellationToken)
            .ConfigureAwait(false);
    }
}
