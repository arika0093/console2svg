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
/// <see cref="HardwareCounter"/>s (CPU instructions retired, total cycles, branches,
/// cache misses) and <see cref="PerfCollectProfiler"/> (a sampling profiler that emits
/// a <c>.trace.zip</c> flame graph for hot-spot analysis). On systems without
/// <c>perf</c>, those are skipped automatically rather than failing the run.
///
/// Set `CONSOLE2SVG_BENCHMARK_PERF=0` to force these off on machines where
/// <c>perf</c> cannot be used (not root, restricted perf_event_paranoid, unsupported
/// kernel or no PMU access, e.g. GitHub-hosted runners): BenchmarkDotNet escalates
/// diagnoser validation warnings to errors
/// (<c>DiagnosersValidator.TreatsWarningsAsErrors</c>), so merely adding these
/// diagnosers there invalidates every benchmark and the run produces no reports.
/// The benchmark CI workflow sets this variable.
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
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddExporter(JsonExporter.Full)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddDiagnoser(CreateDisassemblyDiagnoser())
            .AddColumn(new StaticSvgSizeColumn())
            .AddColumn(new AnimatedSvgSizeColumn());

        // Opt-out via CONSOLE2SVG_BENCHMARK_PERF=0 on machines where perf cannot be
        // used (see the class comment): enabling there invalidates every benchmark.
        if (!IsPerfDisabled() && FindInPath("perf") is not null)
        {
            config
                .AddHardwareCounters(
                    HardwareCounter.InstructionRetired,
                    HardwareCounter.TotalCycles,
                    HardwareCounter.BranchInstructions,
                    HardwareCounter.CacheMisses
                )
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
                exportDiff: false
            )
        );

    private static bool IsPerfDisabled() =>
        Environment.GetEnvironmentVariable("CONSOLE2SVG_BENCHMARK_PERF")?.ToLowerInvariant() switch
        {
            "0" or "false" or "no" or "off" => true,
            _ => false,
        };

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
