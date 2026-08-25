using System.Collections.Concurrent;
using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ConsoleToSvg.Benchmarks.Workloads;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Benchmarks.Columns;

/// <summary>
/// Reports the generated SVG document size (UTF-8 bytes) as a deterministic metric.
///
/// The size is computed by rendering the benchmark's input once in the host process
/// (outside the measured loop), which is valid because SVG output is a pure function of
/// the input session and render options.
/// </summary>
public abstract class SvgSizeColumn : IColumn
{
    // Instance-level cache: each concrete column (static vs animated) must keep its own
    // values. A static cache on this base type would be shared by all subclasses and the
    // static/animated columns would overwrite each other's entries.
    private readonly ConcurrentDictionary<BenchmarkCase, long> _cache = new();

    private readonly bool _animated;

    protected SvgSizeColumn(bool animated) => _animated = animated;

    public abstract string Id { get; }

    public abstract string ColumnName { get; }

    public string Legend => "Size of the generated SVG document in UTF-8 bytes.";

    public UnitType UnitType => UnitType.Dimensionless;

    public bool AlwaysShow => true;

    public ColumnCategory Category => ColumnCategory.Metric;

    public int PriorityInCategory => 0;

    public bool IsNumeric => true;

    public bool IsAvailable(Summary summary) => true;

    public abstract bool IsDefault(Summary summary, BenchmarkCase benchmarkCase);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, summary.Style);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var bytes = _cache.GetOrAdd(benchmarkCase, Compute);
        return bytes.ToString("N0", CultureInfo.InvariantCulture);
    }

    private long Compute(BenchmarkCase benchmarkCase)
    {
        var session = WorkloadCatalog.Resolve(benchmarkCase);
        var options = new SvgRenderOptions { Loop = true };
        var svg = _animated
            ? WorkloadCatalog.RenderAnimated(session, options)
            : WorkloadCatalog.RenderStatic(session, options);
        return WorkloadCatalog.SizeBytes(svg);
    }
}

/// <summary>SVG size for single-frame (static) render benchmarks.</summary>
public sealed class StaticSvgSizeColumn : SvgSizeColumn
{
    public StaticSvgSizeColumn()
        : base(animated: false)
    {
    }

    public override string Id => "StaticSvgBytes";

    public override string ColumnName => "SVG size (static)";

    public override bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) =>
        benchmarkCase.Descriptor.WorkloadMethod.Name is nameof(SvgGenerationBenchmarks.RenderSvg)
            or nameof(RealWorldBenchmarks.RenderStatic);
}

/// <summary>SVG size for animated render benchmarks.</summary>
public sealed class AnimatedSvgSizeColumn : SvgSizeColumn
{
    public AnimatedSvgSizeColumn()
        : base(animated: true)
    {
    }

    public override string Id => "AnimatedSvgBytes";

    public override string ColumnName => "SVG size (animated)";

    public override bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) =>
        benchmarkCase.Descriptor.WorkloadMethod.Name is nameof(RealWorldBenchmarks.RenderAnimated);
}
