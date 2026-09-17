using BenchmarkDotNet.Running;

namespace ConsoleToSvg.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, BenchmarkConfig.Create()).ToList();

        // BenchmarkSwitcher always looks successful to the shell, so surface
        // empty/failed runs explicitly: otherwise a run that produced no reports
        // (e.g. every benchmark invalidated during validation) still exits 0
        // and CI looks green while uploading no CSV.
        if (IsDisplayOnlyArgs(args))
        {
            return 0;
        }

        if (summaries.Count == 0)
        {
            Console.Error.WriteLine("error: BenchmarkDotNet produced no summaries; no benchmarks were run.");
            return 1;
        }

        foreach (var summary in summaries)
        {
            if (summary.HasCriticalValidationErrors
                || summary.GetNumberOfExecutedBenchmarks() == 0
                || summary.Reports.Any(static report => !report.Success))
            {
                Console.Error.WriteLine("error: BenchmarkDotNet run produced no usable reports; see validation errors above.");
                return 1;
            }
        }

        return 0;
    }

    private static bool IsDisplayOnlyArgs(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--list", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--info", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
