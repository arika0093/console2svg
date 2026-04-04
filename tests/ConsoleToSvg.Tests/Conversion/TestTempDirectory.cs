using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleToSvg.Tests.Conversion;

internal sealed class TestTempDirectory : IDisposable
{
    public TestTempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "console2svg-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateExecutable(
        string baseName,
        string outputText,
        string? logPath = null,
        bool writeSecondArgToOutput = false,
        bool writeLastArgToOutput = false
    )
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var path = System.IO.Path.Combine(Path, $"{baseName}.cmd");
            var script = BuildWindowsScript(outputText, logPath, writeSecondArgToOutput, writeLastArgToOutput);
            File.WriteAllText(path, script, new UTF8Encoding(false));
            return path;
        }

        var unixPath = System.IO.Path.Combine(Path, baseName);
        var unixScript = BuildUnixScript(outputText, logPath, writeSecondArgToOutput, writeLastArgToOutput);
        File.WriteAllText(unixPath, unixScript, new UTF8Encoding(false));
        File.SetUnixFileMode(
            unixPath,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute
        );
        return unixPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static string BuildWindowsScript(
        string outputText,
        string? logPath,
        bool writeSecondArgToOutput,
        bool writeLastArgToOutput
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableDelayedExpansion");
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            sb.AppendLine($"echo %* > \"{logPath}\"");
        }

        if (writeSecondArgToOutput)
        {
            sb.AppendLine($"echo {outputText} > \"%~2\"");
        }
        else if (writeLastArgToOutput)
        {
            sb.AppendLine("set LAST=%~1");
            sb.AppendLine(":loop");
            sb.AppendLine("if \"%~2\"==\"\" goto done");
            sb.AppendLine("shift");
            sb.AppendLine("set LAST=%~1");
            sb.AppendLine("goto loop");
            sb.AppendLine(":done");
            sb.AppendLine($"echo {outputText} > \"!LAST!\"");
        }

        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }

    private static string BuildUnixScript(
        string outputText,
        string? logPath,
        bool writeSecondArgToOutput,
        bool writeLastArgToOutput
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/sh");
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            sb.AppendLine($"printf '%s\\n' \"$*\" > '{logPath}'");
        }

        if (writeSecondArgToOutput)
        {
            sb.AppendLine($"printf '%s' '{outputText}' > \"$2\"");
        }
        else if (writeLastArgToOutput)
        {
            sb.AppendLine("last=\"\"");
            sb.AppendLine("for arg in \"$@\"; do");
            sb.AppendLine("  last=\"$arg\"");
            sb.AppendLine("done");
            sb.AppendLine($"printf '%s' '{outputText}' > \"$last\"");
        }

        sb.AppendLine("exit 0");
        return sb.ToString();
    }
}
