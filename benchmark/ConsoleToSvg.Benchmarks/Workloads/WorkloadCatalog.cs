using System;
using System.Text;
using BenchmarkDotNet.Running;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Benchmarks.Workloads;

/// <summary>
/// Resolves a benchmark case back to its input <see cref="RecordingSession"/> so the
/// deterministic SVG-size columns can render the same workload outside the measured loop.
/// </summary>
public static class WorkloadCatalog
{
    public static RecordingSession Resolve(BenchmarkCase benchmarkCase)
    {
        foreach (var item in benchmarkCase.Parameters.Items)
        {
            switch (item.Value)
            {
                case RealFixture fixture:
                    return AsciicastFixture.Load(fixture);
                case WorkloadSize size:
                    return AnsiWorkload.BuildSession(AnsiWorkload.Presets[(int)size], seed: 42);
            }
        }

        throw new InvalidOperationException(
            $"No supported workload parameter on {benchmarkCase.Descriptor.Type.Name}.{benchmarkCase.Descriptor.WorkloadMethod.Name}."
        );
    }

    public static string RenderStatic(RecordingSession session, SvgRenderOptions options) =>
        SvgRenderer.Render(session, options);

    public static string RenderAnimated(RecordingSession session, SvgRenderOptions options) =>
        AnimatedSvgRenderer.Render(session, options);

    public static long SizeBytes(string svg) => Encoding.UTF8.GetByteCount(svg);
}
