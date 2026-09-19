using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ConsoleToSvg.Terminal;

public sealed record RepositorySource(string CloneUrl, string? Ref, string Subdirectory)
{
    public static RepositorySource Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Repository source is empty.", nameof(value));

        var source = value;
        string? gitRef = null;
        var subdirectory = string.Empty;
        var hash = source.IndexOf('#');
        if (hash >= 0)
        {
            var suffix = source[(hash + 1)..];
            source = source[..hash];
            var colon = suffix.IndexOf(':');
            if (colon >= 0)
            {
                gitRef = suffix[..colon];
                subdirectory = suffix[(colon + 1)..];
            }
            else
            {
                gitRef = suffix;
            }
        }
        else if (
            TryParseGitHubShorthand(
                source,
                out var shorthandUrl,
                out var shorthandRef,
                out var shorthandDirectory
            )
        )
        {
            return new RepositorySource(shorthandUrl, shorthandRef, shorthandDirectory);
        }

        return new RepositorySource(source, gitRef, NormalizeDirectory(subdirectory));
    }

    private static bool TryParseGitHubShorthand(
        string value,
        out string url,
        out string? gitRef,
        out string directory
    )
    {
        url = string.Empty;
        gitRef = null;
        directory = string.Empty;
        if (
            value.Contains("://", StringComparison.Ordinal)
            || value.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
        )
            return false;
        var slash = value.IndexOf('/');
        if (slash <= 0)
            return false;
        var owner = value[..slash];
        var rest = value[(slash + 1)..];
        var at = rest.IndexOf('@');
        if (at >= 0)
        {
            var refAndDirectory = rest[(at + 1)..];
            var refSlash = refAndDirectory.IndexOf('/');
            gitRef = refSlash < 0 ? refAndDirectory : refAndDirectory[..refSlash];
            if (refSlash >= 0)
                directory = NormalizeDirectory(refAndDirectory[(refSlash + 1)..]);
            rest = rest[..at];
        }
        var repoSlash = rest.IndexOf('/');
        if (repoSlash >= 0 && string.IsNullOrEmpty(directory))
        {
            directory = NormalizeDirectory(rest[(repoSlash + 1)..]);
            rest = rest[..repoSlash];
        }
        url = $"https://github.com/{owner}/{rest}.git";
        return true;
    }

    private static string NormalizeDirectory(string value) =>
        value.Trim().Trim('/').Replace('\\', '/');
}

public sealed record RepositoryCheckout(string RepositoryRoot, string Root, string Commit);

public static class RepositorySourceAcquirer
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { UserAgent = { ProductInfoHeaderValue.Parse("console2svg") } },
    };

    public static RepositoryCheckout Acquire(RepositorySource source, string destination)
    {
        Directory.CreateDirectory(destination);
        if (TryRunGit("clone", "--quiet", source.CloneUrl, destination))
        {
            if (!string.IsNullOrWhiteSpace(source.Ref))
                RunGit("-C", destination, "checkout", "--quiet", source.Ref);
        }
        else
        {
            DownloadArchive(source, destination);
        }

        var root = string.IsNullOrEmpty(source.Subdirectory)
            ? destination
            : Path.GetFullPath(source.Subdirectory, destination);
        if (!IsPathInside(destination, root) || !Directory.Exists(root))
            throw new InvalidDataException("Repository subdirectory is missing or unsafe.");
        RejectLinkedPathComponents(destination, root);
        return new RepositoryCheckout(destination, root, ReadCommit(destination));
    }

    private static void RejectLinkedPathComponents(string destination, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(destination), Path.GetFullPath(root));
        var current = Path.GetFullPath(destination);
        foreach (
            var segment in relative.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (segment is "." || segment is "..")
            {
                continue;
            }
            current = Path.Combine(current, segment);
            if (
                Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
            {
                throw new InvalidDataException(
                    "Repository subdirectory traverses a linked directory."
                );
            }
        }
    }

    public static void DeleteCheckout(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        var directories = new System.Collections.Generic.List<string> { directory };
        for (var index = 0; index < directories.Count; index++)
        {
            foreach (var file in Directory.EnumerateFiles(directories[index]))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                    File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            foreach (var child in Directory.EnumerateDirectories(directories[index]))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(child);
                else
                    directories.Add(child);
            }
        }
        for (var index = directories.Count - 1; index >= 0; index--)
            Directory.Delete(directories[index]);
    }

    private static string ReadCommit(string repository)
    {
        try
        {
            using var process = Process.Start(
#pragma warning disable S4036
                new ProcessStartInfo("git")
#pragma warning restore S4036
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    ArgumentList = { "-C", repository, "rev-parse", "HEAD" },
                }
            )!;
            process.WaitForExit();
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
    }

    private static bool TryRunGit(params string[] arguments)
    {
        try
        {
            RunGit(arguments);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static void RunGit(params string[] arguments)
    {
#pragma warning disable S4036
        var startInfo = new ProcessStartInfo("git")
#pragma warning restore S4036
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process =
            Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd().Trim());
    }

    private static void DownloadArchive(RepositorySource source, string destination)
    {
        using var response = HttpClient.GetAsync(GetArchiveUrl(source)).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var archive = response.Content.ReadAsStream();
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var root = string.Empty;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            var relativePath = NormalizeArchivePath(entry.Name, ref root);
            if (relativePath.Length == 0)
                continue;
            var target = Path.GetFullPath(Path.Combine(destination, relativePath));
            if (!IsPathInside(destination, target))
                throw new InvalidDataException("Repository archive contains an unsafe path.");
            if (entry.EntryType is TarEntryType.Directory)
                Directory.CreateDirectory(target);
            else if (entry.DataStream is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = File.Create(target);
                entry.DataStream.CopyTo(output);
            }
        }
    }

    private static string GetArchiveUrl(RepositorySource source)
    {
        var repository = source.CloneUrl;
        if (repository.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            repository = "https://github.com/" + repository["git@github.com:".Length..];
        if (
            !Uri.TryCreate(repository, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "Git is unavailable and archive fallback is only supported for GitHub sources."
            );
        var path = uri.AbsolutePath.Trim('/').TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        var suffix = string.IsNullOrWhiteSpace(source.Ref)
            ? string.Empty
            : "/" + Uri.EscapeDataString(source.Ref);
        return $"https://api.github.com/repos/{path}/tarball{suffix}";
    }

    private static string NormalizeArchivePath(string path, ref string root)
    {
        path = path.Replace('\\', '/').Trim('/');
        var separator = path.IndexOf('/');
        if (separator < 0)
            return string.Empty;
        if (root.Length == 0)
            root = path[..separator];
        if (!path.StartsWith(root + "/", StringComparison.Ordinal))
            throw new InvalidDataException("Repository archive contains multiple roots.");
        return path[(root.Length + 1)..];
    }

    private static bool IsPathInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
