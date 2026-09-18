using BenchmarkDotNet.Attributes;
using ConsoleToSvg.QuickLeaks;
using Filter = ConsoleToSvg.QuickLeaks.QuickLeaks;

namespace ConsoleToSvg.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class QuickLeaksBenchmarks
{
    private readonly string _ordinaryOutput = string.Join(
        '\n',
        Enumerable.Repeat(
            "Building project src/ConsoleToSvg.Core and running 128 tests: all tests passed in 1.24s.",
            64
        )
    );

    private readonly string _outputWithSecrets = string.Join(
        '\n',
        Enumerable.Repeat(
            "PASSWORD=123456 github token ghp_abcdefghijklmnopqrstuvwxyz1234567890",
            16
        )
    );

    [Benchmark(Baseline = true)]
    public IReadOnlyList<QuickLeaksFinding> OrdinaryOutput() => Filter.Scan(_ordinaryOutput);

    [Benchmark]
    public IReadOnlyList<QuickLeaksFinding> OutputWithSecrets() =>
        Filter.Scan(_outputWithSecrets);
}
