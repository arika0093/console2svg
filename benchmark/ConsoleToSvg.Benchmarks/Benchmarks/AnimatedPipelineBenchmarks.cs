using BenchmarkDotNet.Attributes;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Benchmarks;

[ShortRunJob]
public class AnimatedPipelineBenchmarks
{
    [ParamsAllValues]
    public RealFixture Fixture { get; set; }

    private RecordingSession _session = null!;
    private SvgRenderOptions _options = null!;
    private IReadOnlyList<TerminalFrame> _frames = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = AsciicastFixture.Load(Fixture);
        _options = new SvgRenderOptions { Loop = true };
        _frames = CreateEmulator().ReplayFrames(_session);
    }

    [Benchmark]
    public ScreenBuffer ReplayWithoutSnapshots() =>
        CreateEmulator().Replay(_session, _session.Events.Count - 1);

    [Benchmark]
    public IReadOnlyList<TerminalFrame> ReplayWithSnapshots() =>
        CreateEmulator().ReplayFrames(_session);

#if !CONSOLE_TO_SVG_BASELINE
    [Benchmark]
    public ulong ReplayWithFrameSignatures()
    {
        var frames = CreateEmulator().ReplayFrames(_session);
        var signature = 0UL;
        for (var i = 0; i < frames.Count; i++)
        {
            signature ^= frames[i].Buffer.GetVisualSignature();
        }

        return signature;
    }
#endif

    [Benchmark]
    public string RenderFrames() =>
        AnimatedSvgRenderer.RenderFrames(
            _frames,
            new SvgRenderOptions { Loop = true, VideoFps = 0 });

    [Benchmark]
    public string CompleteAnimatedRender() =>
        AnimatedSvgRenderer.Render(_session, _options);

    private TerminalEmulator CreateEmulator() =>
        new(
            _session.Header.width,
            _session.Header.height,
            Theme.Resolve("dark"));
}
