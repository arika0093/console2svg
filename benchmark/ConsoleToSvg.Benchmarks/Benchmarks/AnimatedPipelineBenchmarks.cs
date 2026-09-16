using BenchmarkDotNet.Attributes;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Benchmarks;

public class AnimatedPipelineBenchmarks
{
    [ParamsAllValues]
    public RealFixture Fixture { get; set; }

    private RecordingSession _session = null!;
    private SvgRenderOptions _options = null!;
    private IReadOnlyList<TerminalFrame> _productionFrames = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AsciicastFixture.Load(Fixture);
        _options = new SvgRenderOptions { Loop = true };
#if !CONSOLE_TO_SVG_BASELINE
        _options.TerminalTheme = Theme.Resolve("dark");
        _productionFrames = AnimatedSvgRenderer.PrepareFrames(_session, _options);
#else
        _productionFrames = CreateEmulator().ReplayFrames(_session);
#endif
    }

    [Benchmark]
    public ScreenBuffer ReplayWithoutSnapshots() =>
        CreateEmulator().Replay(_session, _session.Events.Count - 1);

#if !CONSOLE_TO_SVG_BASELINE
    [Benchmark]
    public IReadOnlyList<TerminalFrame> PrepareProductionFrames() =>
        AnimatedSvgRenderer.PrepareFrames(_session, _options);
#endif

    [Benchmark]
    public string RenderPreparedFrames() =>
        AnimatedSvgRenderer.RenderFrames(_productionFrames, _options);

    [Benchmark]
    public string CompleteAnimatedRender() => AnimatedSvgRenderer.Render(_session, _options);

    private TerminalEmulator CreateEmulator() =>
#if !CONSOLE_TO_SVG_BASELINE
        new(_session.Header.width, _session.Header.height, _options.TerminalTheme!);
#else
        new(_session.Header.width, _session.Header.height, Theme.Resolve("dark"));
#endif
}
