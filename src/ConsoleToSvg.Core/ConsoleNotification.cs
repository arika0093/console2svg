using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Core;

/// <summary>
/// Default <see cref="INotification"/> implementation that writes messages to <see cref="Console.Error"/>.
/// Used in normal (non-interactive) mode.
/// </summary>
public sealed class ConsoleNotification : INotification
{
    /// <summary>Shared singleton instance.</summary>
    public static ConsoleNotification Instance { get; } = new();

    private ConsoleNotification() { }

    /// <inheritdoc />
    public async Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        await Console.Error.WriteLineAsync(message.AsMemory(), cancellationToken);
    }
}
