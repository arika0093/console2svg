using System;

namespace ConsoleToSvg.Tests.Conversion;

internal sealed class EnvironmentScope : IDisposable
{
    private static readonly System.Threading.SemaphoreSlim SyncRoot = new(1, 1);
    private readonly System.Collections.Generic.Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private bool _disposed;

    public EnvironmentScope()
    {
        SyncRoot.Wait();
    }

    public void Set(string key, string? value)
    {
        if (!_values.ContainsKey(key))
        {
            _values[key] = Environment.GetEnvironmentVariable(key);
        }

        Environment.SetEnvironmentVariable(key, value);
    }

    public void PrependPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH");
        Set("PATH", string.IsNullOrWhiteSpace(current) ? directory : directory + System.IO.Path.PathSeparator + current);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var pair in _values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        _disposed = true;
        SyncRoot.Release();
    }
}
