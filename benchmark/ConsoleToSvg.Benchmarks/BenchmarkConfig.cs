using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using ConsoleToSvg.Benchmarks.Columns;
using Iced.Intel;

namespace ConsoleToSvg.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration.
///
/// Always enables:
///   <list type="bullet">
///     <item><see cref="MemoryDiagnoser"/> — allocated bytes + GC collections.</item>
///     <item><see cref="DisassemblyDiagnoser"/> — machine code (per-benchmark .asm files).</item>
///   </list>
/// Additionally, when the Linux <c>perf</c> tool is available, enables
/// <see cref="HardwareCounter"/>s (CPU instructions retired, total cycles, branches, cache
/// misses) and <see cref="PerfCollectProfiler"/> (a sampling profiler that emits a
/// <c>.trace.zip</c> flame graph for hot-spot analysis). On systems without <c>perf</c>,
/// those are skipped automatically rather than failing the run.
///
/// Two custom columns (<see cref="StaticSvgSizeColumn"/>, <see cref="AnimatedSvgSizeColumn"/>)
/// report the generated SVG document size in bytes.
///
/// Results are exported as GitHub-flavored Markdown, HTML, JSON, and CSV so they can be
/// diffed between runs and versions.
/// </summary>
public static class BenchmarkConfig
{
    public static IConfig Create()
    {
        // DefaultConfig already ships Markdown (console + GitHub), HTML, and CSV
        // exporters; add only the full JSON export to keep results diffable.
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddExporter(JsonExporter.Full)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddDiagnoser(CreateDisassemblyDiagnoser())
            .AddColumn(new StaticSvgSizeColumn())
            .AddColumn(new AnimatedSvgSizeColumn());

        if (FindInPath("perf") is not null)
        {
            config
                .AddHardwareCounters(
                    HardwareCounter.InstructionRetired,
                    HardwareCounter.TotalCycles,
                    HardwareCounter.BranchInstructions,
                    HardwareCounter.CacheMisses)
                .AddDiagnoser(PerfCollectProfiler.Default);
        }

        return config;
    }

    private static DisassemblyDiagnoser CreateDisassemblyDiagnoser() =>
        new(
            new DisassemblyDiagnoserConfig(
                maxDepth: 2,
                syntax: DisassemblySyntax.Intel,
                filters: Array.Empty<string>(),
                formatterOptions: new FormatterOptions(),
                printSource: true,
                printInstructionAddresses: false,
                exportGithubMarkdown: true,
                exportHtml: false,
                exportCombinedDisassemblyReport: true,
                exportDiff: false));

    private static string? FindInPath(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var directory in paths)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
