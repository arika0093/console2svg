using System.IO;
using BenchmarkDotNet.Attributes;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Benchmarks;

#if !CONSOLE_TO_SVG_BASELINE
/// <summary>
/// Measures the two production options that most directly control animated workload:
/// retained frame rate and automatic secret masking. Btop is used as a stable,
/// update-heavy fixture while the broader fixture matrix remains in
/// <see cref="RealWorldBenchmarks"/>.
/// </summary>
public class AnimatedOptionsBenchmarks
{
    [Params(RealFixture.Btop)]
    public RealFixture Fixture { get; set; }

    [Params(0d, 12d, 30d, 60d)]
    public double VideoFps { get; set; }

    [Params(false, true)]
    public bool AutoMask { get; set; }

    private RecordingSession _session = null!;
    private SvgRenderOptions _options = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AsciicastFixture.Load(Fixture);
        _options = new SvgRenderOptions
        {
            Loop = true,
            VideoFps = VideoFps,
            MaskAuto = AutoMask,
            TerminalTheme = Theme.Resolve("dark"),
        };
    }

    [Benchmark]
    public void WriteAnimated() => AnimatedSvgRenderer.Write(TextWriter.Null, _session, _options);
}
#endif
