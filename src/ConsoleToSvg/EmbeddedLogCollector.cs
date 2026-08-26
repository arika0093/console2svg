using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg;

/// <summary>
/// Collects verbose application log entries in memory so they can be embedded in an SVG.
/// </summary>
/// <remarks>
/// The collector is both an <see cref="ILoggerProvider"/> and the shared <see cref="ILogger"/>
/// returned for every logging category. Entries are safe to append concurrently.
/// </remarks>
internal sealed class EmbeddedLogCollector : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => this;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var entry = $"[{DateTimeOffset.Now:O}] [{logLevel}] {formatter(state, exception)}";
        if (exception is not null)
        {
            entry += Environment.NewLine + exception;
        }
        _entries.Enqueue(entry);
    }

    /// <summary>
    /// Encodes the collected log text as UTF-8 Base64 for storage in SVG metadata.
    /// </summary>
    /// <returns>
    /// The Base64-encoded log text, or an empty Base64 value when no entries were collected.
    /// </returns>
    public string GetBase64()
    {
        var text = string.Join(Environment.NewLine, _entries);
        if (text.Length > 0)
        {
            text += Environment.NewLine;
        }
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    /// <inheritdoc />
    public void Dispose() { }
}
