using BenchmarkDotNet.Attributes;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Benchmarks;

/// <summary>
/// Benchmarks the SVG generation pipeline on synthetic (in-memory) workloads:
///   <list type="bullet">
///     <item><see cref="RenderSvg"/> — full pipeline (ANSI parse + replay + SVG string).</item>
///     <item><see cref="ParseOnly"/> — ANSI replay/terminal emulation only (no SVG).</item>
///   </list>
/// </summary>
public class SvgGenerationBenchmarks
{
    [ParamsAllValues]
    public WorkloadSize Size { get; set; }

    private RecordingSession _session = null!;
    private RecordingSession _wideCharacterSession = null!;
    private RecordingSession _scrollStressSession = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AnsiWorkload.BuildSession(
            AnsiWorkload.Presets[(int)Size],
            seed: 42);
        _wideCharacterSession = AnsiWorkload.BuildWideCharacterSession(
            AnsiWorkload.Presets[(int)Size],
            seed: 42);
        _scrollStressSession = AnsiWorkload.BuildScrollStressSession(
            AnsiWorkload.Presets[(int)Size]);
    }

    /// <summary>Render the recorded session to an SVG document string.</summary>
    [Benchmark]
    public string RenderSvg() =>
        SvgRenderer.Render(_session, new SvgRenderOptions());

    /// <summary>Replay the session through the terminal emulator only (no SVG emission).</summary>
    [Benchmark]
    public ScreenBuffer ParseOnly()
        => Replay(_session);

    /// <summary>Parse a screen-filling workload consisting only of wide characters.</summary>
    [Benchmark]
    public ScreenBuffer ParseWideCharacters()
        => Replay(_wideCharacterSession);

    /// <summary>Parse continuous output that intentionally drives full-screen scrolling.</summary>
    [Benchmark]
    public ScreenBuffer ParseScrollStress()
        => Replay(_scrollStressSession);

    private static ScreenBuffer Replay(RecordingSession session)
    {
        var emulator = new TerminalEmulator(
            session.Header.width,
            session.Header.height,
            Theme.Resolve("dark"));
        return emulator.Replay(session, session.Events.Count - 1);
    }
}
