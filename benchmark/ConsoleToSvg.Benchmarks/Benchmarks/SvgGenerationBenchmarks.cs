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

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AnsiWorkload.BuildSession(
            AnsiWorkload.Presets[(int)Size],
            seed: 42);
    }

    /// <summary>Render the recorded session to an SVG document string.</summary>
    [Benchmark]
    public string RenderSvg() =>
        SvgRenderer.Render(_session, new SvgRenderOptions());

    /// <summary>Replay the session through the terminal emulator only (no SVG emission).</summary>
    [Benchmark]
    public ScreenBuffer ParseOnly()
    {
        var emulator = new TerminalEmulator(
            _session.Header.width,
            _session.Header.height,
            Theme.Resolve("dark"));
        return emulator.Replay(_session, _session.Events.Count - 1);
    }
}
