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

                    while (cell < end)
                    {
                        cell += AppendCell(rng, sb, end - cell);
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

    public static RecordingSession BuildWideCharacterSession(Preset preset, int seed)
    {
        var session = new RecordingSession(preset.Width, preset.Height);
        var rng = new Random(seed);
        for (var frame = 0; frame < preset.Frames; frame++)
        {
            var sb = new StringBuilder(preset.Width * preset.Height);
            sb.Append("\x1b[H");
            for (var row = 0; row < preset.Height; row++)
            {
                for (var col = 0; col + 1 < preset.Width; col += 2)
                {
                    sb.Append((char)(0x3041 + rng.Next(0x53)));
                }

                if (row < preset.Height - 1)
                {
                    sb.Append("\r\n");
                }
            }

            session.AddEvent((frame + 1) * 0.05, sb.ToString());
        }

        return session;
    }

    public static RecordingSession BuildScrollStressSession(Preset preset)
    {
        var session = new RecordingSession(preset.Width, preset.Height);
        var line = new string('x', preset.Width - 1);
        var sb = new StringBuilder((line.Length + 2) * preset.Frames * preset.Height);
        for (var lineIndex = 0; lineIndex < preset.Frames * preset.Height; lineIndex++)
        {
            sb.Append(line).Append("\r\n");
        }

        session.AddEvent(0.05, sb.ToString());
        return session;
    }

    private static int AppendCell(Random rng, StringBuilder sb, int remainingWidth)
    {
        if (remainingWidth >= 2 && rng.Next(100) < 6)
        {
            // Hiragana (East Asian Wide): exercises the wide-cell rendering path.
            sb.Append((char)(0x3041 + rng.Next(0x53)));
            return 2;
        }

        sb.Append((char)('a' + rng.Next(26)));
        return 1;
    }
}
