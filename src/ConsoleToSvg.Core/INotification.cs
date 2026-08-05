using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Core;

/// <summary>
/// Abstraction for displaying progress and status messages during conversion operations.
/// Implementations can route messages to the console (normal mode) or to a UI notification bar (interactive mode).
/// </summary>
public interface INotification
{
    /// <summary>
    /// Displays a message to the user.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyAsync(string message, CancellationToken cancellationToken);
}
