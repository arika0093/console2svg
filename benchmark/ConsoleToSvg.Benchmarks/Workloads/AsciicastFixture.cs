using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Benchmarks.Workloads;

/// <summary>Pre-recorded real-world terminal sessions (see <c>benchmark/fixtures/*.cast</c>).</summary>
public enum RealFixture
{
    Nyancat = 0,
    Cmatrix = 1,
    Btop = 2,
}

/// <summary>
/// Loads pre-recorded asciicast fixtures without depending on the
/// <c>ConsoleToSvg.Record</c> assembly. Parsing the v2 asciicast format here keeps the
/// benchmark coupled to <c>ConsoleToSvg.Core</c> only, which is what makes the
/// released-vs-source comparison possible (the released Core assembly is self-contained).
/// </summary>
public static class AsciicastFixture
{
    private const string FixtureDirectory = "fixtures";

    public static string GetFileName(RealFixture fixture) =>
        fixture switch
        {
            RealFixture.Nyancat => "nyancat.cast",
            RealFixture.Cmatrix => "cmatrix.cast",
            RealFixture.Btop => "btop.cast",
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown fixture."),
        };

    public static RecordingSession Load(RealFixture fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FixtureDirectory, GetFileName(fixture));
        return ReadAsciicast(path);
    }

    private static RecordingSession ReadAsciicast(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            4096,
            leaveOpen: true
        );

        var headerLine = reader.ReadLine()
            ?? throw new InvalidDataException($"Invalid asciicast: missing header in '{path}'.");
        var header = JsonSerializer.Deserialize<AsciicastHeader>(headerLine)
            ?? throw new InvalidDataException($"Invalid asciicast header in '{path}'.");

        var session = new RecordingSession(header);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
            {
                continue;
            }

            session.Events.Add(
                new AsciicastEvent
                {
                    Time = root[0].GetDouble(),
                    Type = root[1].GetString() ?? "o",
                    Data = root[2].GetString() ?? string.Empty,
                }
            );
        }

        return session;
    }
}
