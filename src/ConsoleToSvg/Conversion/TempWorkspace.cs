using System;
using System.IO;

namespace ConsoleToSvg.Conversion;

internal sealed class TempWorkspace : IDisposable
{
    private readonly string _rootPath;
    private bool _disposed;

    public TempWorkspace()
    {
        _rootPath = Path.Combine(
            Path.GetTempPath(),
            "console2svg",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(_rootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetFilePath(string name) => Path.Combine(_rootPath, name);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }

        _disposed = true;
    }
}
