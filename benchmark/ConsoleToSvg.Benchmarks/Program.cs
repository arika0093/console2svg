using BenchmarkDotNet.Running;

namespace ConsoleToSvg.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, BenchmarkConfig.Create());
        return 0;
    }
}
