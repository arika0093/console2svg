using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Default <see cref="IProgressReporter"/> implementation that writes messages to <see cref="Console.Error"/>.
/// Used in normal (non-interactive) mode.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter
{
    /// <summary>Shared singleton instance.</summary>
    public static ConsoleProgressReporter Instance { get; } = new();

    private ConsoleProgressReporter() { }

    /// <inheritdoc />
    public async Task ReportAsync(string message, CancellationToken cancellationToken)
    {
        await Console.Error.WriteLineAsync(message.AsMemory(), cancellationToken);
    }
}
