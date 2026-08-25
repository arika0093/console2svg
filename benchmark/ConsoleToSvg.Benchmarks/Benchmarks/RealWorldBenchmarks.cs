using BenchmarkDotNet.Attributes;
using System.IO;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Benchmarks;

/// <summary>
/// Benchmarks SVG generation from real, pre-recorded terminal sessions
/// (btop, nyancat, cmatrix — see <c>benchmark/fixtures/*.cast</c>).
///
/// A short job is used because animated rendering of a multi-second capture can take
/// hundreds of milliseconds to seconds per operation.
/// </summary>
[ShortRunJob]
public class RealWorldBenchmarks
{
    [ParamsAllValues]
    public RealFixture Fixture { get; set; }

    private RecordingSession _session = null!;
    private SvgRenderOptions _options = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AsciicastFixture.Load(Fixture);
        // Loop=true mirrors the CLI default for animated output; SvgRenderer.Render
        // ignores it, so the static path is unaffected.
        _options = new SvgRenderOptions { Loop = true };
    }

    /// <summary>Render the session to a single static SVG document.</summary>
    [Benchmark]
    public string RenderStatic() => SvgRenderer.Render(_session, _options);

    /// <summary>Render the session to an animated SVG document (every visual frame).</summary>
    [Benchmark]
    public string RenderAnimated() => AnimatedSvgRenderer.Render(_session, _options);

    /// <summary>Stream a static SVG without materializing the final string.</summary>
    [Benchmark]
    public void WriteStatic() => SvgRenderer.Write(TextWriter.Null, _session, _options);

    /// <summary>Stream an animated SVG without materializing the final string.</summary>
    [Benchmark]
    public void WriteAnimated() => AnimatedSvgRenderer.Write(TextWriter.Null, _session, _options);
}
