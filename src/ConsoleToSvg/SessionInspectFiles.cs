using System;
using System.Diagnostics;
using System.IO;

namespace ConsoleToSvg;

internal static class SessionInspectFiles
{
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    internal const string InspectFileName = "inspect.svg";

    internal static string GetInspectRoot(string? tempBase = null)
    {
        var basePath = tempBase ?? Path.GetTempPath();
        return Path.Combine(
            basePath,
            $"console2svg-inspect-{SanitizeUserName(Environment.UserName)}"
        );
    }

    internal static string CreateInspectPath(string? tempBase = null, DateTimeOffset? now = null)
    {
        var root = GetInspectRoot(tempBase);
        Directory.CreateDirectory(root);
        HardenPrivateDirectory(root);
        SweepStaleInspectFiles(now ?? DateTimeOffset.UtcNow, tempBase, Retention);
        var directory = Path.Combine(root, $"inspect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        HardenPrivateDirectory(directory);
        return Path.Combine(directory, InspectFileName);
    }

    internal static int SweepStaleInspectFiles(
        DateTimeOffset now,
        string? tempBase = null,
        TimeSpan? retention = null
    )
    {
        var limit = retention ?? Retention;
        var root = GetInspectRoot(tempBase);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(root);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (var entry in entries)
        {
            DateTimeOffset lastWrite;
            try
            {
                if (Directory.Exists(entry))
                {
                    lastWrite = Directory.GetLastWriteTimeUtc(entry);
                    foreach (var inner in Directory.GetFileSystemEntries(entry))
                    {
                        var innerWrite = Directory.Exists(inner)
                            ? Directory.GetLastWriteTimeUtc(inner)
                            : File.GetLastWriteTimeUtc(inner);
                        if (innerWrite > lastWrite)
                        {
                            lastWrite = innerWrite;
                        }
                    }
                }
                else
                {
                    lastWrite = File.GetLastWriteTimeUtc(entry);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(exception);
                continue;
            }

            if (now - lastWrite < limit)
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
                removed++;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(exception);
            }
        }

        return removed;
    }

    internal static void HardenPrivateFile(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            Debug.WriteLine(exception);
        }
    }

    private static void HardenPrivateDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            Debug.WriteLine(exception);
        }
    }

    private static string SanitizeUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "shared";
        }

        var builder = new System.Text.StringBuilder(userName.Length);
        foreach (var ch in userName.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');
        }

        var sanitized = builder.ToString().Trim('.', ' ', '_');
        if (sanitized.Length == 0)
        {
            return "shared";
        }

        return sanitized.Length > 32 ? sanitized[..32] : sanitized;
    }
}
