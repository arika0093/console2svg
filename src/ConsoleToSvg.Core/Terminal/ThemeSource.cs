using System;

namespace ConsoleToSvg.Terminal;

public sealed record ThemeSource(string CloneUrl, string? Ref, string Subdirectory)
{
    public static ThemeSource Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Theme source is empty.", nameof(value));

        var source = value;
        string? gitRef = null;
        string subdirectory = string.Empty;
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
            return new ThemeSource(shorthandUrl, shorthandRef, shorthandDirectory);
        }

        return new ThemeSource(source, gitRef, NormalizeDirectory(subdirectory));
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
