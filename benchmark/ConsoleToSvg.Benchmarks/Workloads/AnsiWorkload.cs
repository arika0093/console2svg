using System.Text;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Benchmarks.Workloads;

/// <summary>Synthetic workload sizes mapped to <see cref="AnsiWorkload.Presets"/>.</summary>
public enum WorkloadSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// Builds deterministic, representative terminal workloads for benchmarking.
///
/// Each workload is a <see cref="RecordingSession"/> full of full-screen repaints
/// mixing SGR color codes (16-color, 256-color, and 24-bit truecolor), bold /
/// italic / underline attributes, cursor positioning, and a small proportion of
/// East-Asian-Wide characters to exercise the wide-cell rendering path.
/// </summary>
public static class AnsiWorkload
{
    public readonly record struct Preset(int Width, int Height, int Frames);

    /// <summary>Small, medium, and large workloads, indexed by <see cref="WorkloadSize"/>.</summary>
    public static readonly Preset[] Presets =
    [
        new(80, 24, 50),    // small  (~96K cells,  ~0.1MB of ANSI)
        new(120, 40, 100),  // medium (~480K cells, ~0.5MB of ANSI)
        new(160, 50, 200),  // large  (~1.6M cells, ~1.7MB of ANSI)
    ];

    private static readonly string[] SgrPalette =
    [
        "\x1b[0m",
        "\x1b[1m",
        "\x1b[3m",
        "\x1b[4m",
        "\x1b[31m",
        "\x1b[32m",
        "\x1b[33m",
        "\x1b[34m",
        "\x1b[35m",
        "\x1b[36m",
        "\x1b[38;5;208m",
        "\x1b[38;5;45m",
        "\x1b[38;2;255;128;64m",
        "\x1b[38;2;64;128;255m",
    ];

    public static RecordingSession BuildSession(Preset preset, int seed) =>
        BuildSession(preset.Width, preset.Height, preset.Frames, seed);

    public static RecordingSession BuildSession(int width, int height, int frameCount, int seed)
    {
        var session = new RecordingSession(width, height);
        var rng = new Random(seed);
        double time = 0.0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sb = new StringBuilder(capacity: width * height * 4);
            sb.Append("\x1b[H\x1b[?25l");

            for (var row = 0; row < height; row++)
            {
                // Start each row with a color, then emit runs of text. This mirrors real
                // terminal output (runs of plain text with occasional SGR changes) rather
                // than wrapping every single cell in its own escape sequence.
                sb.Append(SgrPalette[rng.Next(SgrPalette.Length)]);
                var cell = 0;
                while (cell < width)
                {
                    var runLength = 1 + rng.Next(16);
                    var end = Math.Min(width, cell + runLength);
                    if (rng.Next(4) == 0)
                    {
                        sb.Append(SgrPalette[rng.Next(SgrPalette.Length)]);
                    }

                    for (; cell < end; cell++)
                    {
                        AppendCell(rng, sb);
                    }
                }

                sb.Append("\x1b[0m");
                if (row < height - 1)
                {
                    sb.Append("\r\n");
                }
            }

            time += 0.05;
            session.AddEvent(time, sb.ToString());
        }

        return session;
    }

    private static void AppendCell(Random rng, StringBuilder sb)
    {
        if (rng.Next(100) < 6)
        {
            // Hiragana (East Asian Wide): exercises the wide-cell rendering path.
            sb.Append((char)(0x3041 + rng.Next(0x53)));
        }
        else
        {
            sb.Append((char)('a' + rng.Next(26)));
        }
    }
}
