using System.IO;
using BenchmarkDotNet.Attributes;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Benchmarks;

#if !CONSOLE_TO_SVG_BASELINE
/// <summary>Separates the hot stages after production frame replay and reduction.</summary>
public class AnimatedRenderingStageBenchmarks
{
    private TerminalFrame[] _frames = null!;
    private SvgRenderOptions _options = null!;
    private Theme _theme = null!;
    private SvgDocumentBuilder.Context _context;
    private SvgStyleRegistry _styles = null!;
    private int[][] _rowDefinitions = null!;
    private double _totalDuration;

    [Params(RealFixture.Btop)]
    public RealFixture Fixture { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var session = AsciicastFixture.Load(Fixture);
        _theme = Theme.Resolve("dark");
        _options = new SvgRenderOptions { Loop = true, TerminalTheme = _theme };
        _frames = AnimatedSvgRenderer.PrepareFrames(session, _options).ToArray();
        _context = SvgRenderShared.CreateContext(
            _frames[0].Buffer,
            _options,
            includeScrollback: false,
            commandHeaderRows: 0
        );
        _styles = new SvgStyleRegistry();
        SvgDocumentBuilder.CollectTextStyles(_frames, _context, _styles);
        _rowDefinitions = BuildRowDefinitions();
        _totalDuration = Math.Max(0.05d, _frames[^1].Time) + _options.VideoSleep;

        var uniqueDefinitions = _rowDefinitions.SelectMany(static rows => rows).Max() + 1;
        Console.WriteLine(
            $"RetainedFrames={_frames.Length}; Rows={_rowDefinitions[0].Length}; UniqueRowDefinitions={uniqueDefinitions}"
        );
    }

    [Benchmark]
    public object CollectTextStyles()
    {
        var styles = new SvgStyleRegistry();
        SvgDocumentBuilder.CollectTextStyles(_frames, _context, styles);
        return styles;
    }

    [Benchmark]
    public int[][] BuildAnimatedRowDefinitions() => BuildRowDefinitions();

    [Benchmark]
    public void AppendAnimatedRows()
    {
        var svgWriter = new SvgWriter(TextWriter.Null);
        SvgDocumentBuilder.AppendAnimatedRows(
            svgWriter,
            _frames,
            _rowDefinitions,
            _context,
            _theme,
            _totalDuration,
            _options.VideoFadeOut,
            _options.Loop
        );
    }

    [Benchmark]
    public void WritePreparedFrames() =>
        AnimatedSvgRenderer.WriteFrames(TextWriter.Null, _frames, _options);

    private int[][] BuildRowDefinitions()
    {
        var svgWriter = new SvgWriter(TextWriter.Null);
        return SvgDocumentBuilder.AppendAnimatedRowDefs(
            svgWriter,
            _frames,
            _context,
            _theme,
            _styles,
            _options.LengthAdjust,
            maskPatterns: _options.MaskPatterns,
            autoMask: _options.MaskAuto,
            autoMaskMode: _options.AutoMaskMode
        );
    }
}
#endif
