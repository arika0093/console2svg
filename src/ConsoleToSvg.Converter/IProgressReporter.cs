using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Abstraction for reporting progress and status messages during long-running operations.
/// Implementations can route messages to different destinations (console, UI, logs, etc.).
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports a progress or status message.
    /// </summary>
    /// <param name="message">The message to report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReportAsync(string message, CancellationToken cancellationToken);
}
