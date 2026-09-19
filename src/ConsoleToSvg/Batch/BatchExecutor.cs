using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Batch;

/// <summary>
/// Pure helpers for batch execution. Kept free of PTY/rendering so they stay
/// unit-testable; <c>Program.Batch</c> wires them to the real pipeline.
/// </summary>
public static class BatchExecutor
{
    /// <summary>
    /// Builds the shell script run under PTY. Setup output is redirected to a log
    /// file; a marker line plus screen clear separates setup from capture.
    /// </summary>
    public static string BuildScript(BatchParsedJob job, string? setupLogPath, bool isWindows)
    {
        if (isWindows)
        {
            return BuildWindowsScript(job, setupLogPath);
        }

        var builder = new StringBuilder();
        builder.AppendLine("set -e");

        if (job.Setup.Length > 0 && setupLogPath is not null)
        {
            builder.AppendLine("{");
            builder.Append(job.Setup);
            if (!job.Setup.EndsWith('\n'))
            {
                builder.AppendLine();
            }
            builder.AppendLine($"}} > \"{setupLogPath}\" 2>&1");
        }

        builder.AppendLine($"printf '{BatchMarkdown.CaptureMarker}\\n'");
        builder.AppendLine("printf '\\033[2J\\033[H'");
        builder.Append(job.Capture);
        if (!job.Capture.EndsWith('\n'))
        {
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildWindowsScript(BatchParsedJob job, string? setupLogPath)
    {
        // cmd.exe /c ignores everything after the first newline in its payload,
        // so the Windows script must be a single &-joined command line.
        var segments = new List<string>();
        if (job.Setup.Length > 0 && setupLogPath is not null)
        {
            var setup = string.Join(" & ", SplitScriptLines(job.Setup));
            segments.Add($"( {setup} ) > \"{setupLogPath}\" 2>&1 || exit /b 1");
        }

        segments.Add($"echo {BatchMarkdown.CaptureMarker}");
        segments.Add("cls");
        segments.AddRange(SplitScriptLines(job.Capture));
        return string.Join(" & ", segments);
    }

    private static IEnumerable<string> SplitScriptLines(string script) =>
        script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

    /// <summary>
    /// Drops everything up to the capture marker and rebases the timeline so the
    /// capture starts at zero on a cleared screen. Returns false when the marker
    /// is missing (setup never finished).
    /// </summary>
    public static bool TryTrimBeforeMarker(RecordingSession session)
    {
        var index = -1;
        for (var i = 0; i < session.Events.Count; i++)
        {
            if (
                session
                    .Events[i]
                    .Data.Contains(BatchMarkdown.CaptureMarker, StringComparison.Ordinal)
            )
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        var markerEvent = session.Events[index];
        var markerAt = markerEvent.Data.IndexOf(
            BatchMarkdown.CaptureMarker,
            StringComparison.Ordinal
        );
        var remainder = markerEvent.Data[(markerAt + BatchMarkdown.CaptureMarker.Length)..];
        var baseTime = markerEvent.Time;
        var kept = new List<AsciicastEvent>();
        if (remainder.Length > 0)
        {
            kept.Add(
                new AsciicastEvent
                {
                    Time = 0,
                    Type = markerEvent.Type,
                    Data = remainder,
                }
            );
        }
        kept.AddRange(
            session
                .Events.Skip(index + 1)
                .Select(e => new AsciicastEvent
                {
                    Time = Math.Max(0, e.Time - baseTime),
                    Type = e.Type,
                    Data = e.Data,
                })
        );
        session.Events.Clear();
        session.Events.Add(
            new AsciicastEvent
            {
                Time = 0,
                Type = "o",
                Data = "\x1b[2J\x1b[H",
            }
        );
        session.Events.AddRange(kept);
        return true;
    }

    /// <summary>Resolves a marker -o value under the output directory.</summary>
    public static string? ResolveOutput(string outputDir, string outputRelative)
    {
        var normalized = NormalizePath(outputRelative);
        if (
            normalized.StartsWith('/', StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            || normalized.Split('/').Any(segment => segment == "..")
        )
        {
            return null;
        }
        var combined = Path.GetFullPath(
            Path.Combine(outputDir, normalized.Replace('/', Path.DirectorySeparatorChar))
        );
        return IsPathInside(outputDir, combined) ? combined : null;
    }

    /// <summary>Markdown-relative link target for a generated asset.</summary>
    public static string Relativize(string mdPath, string outputAbs)
    {
        var mdDir = Path.GetDirectoryName(Path.GetFullPath(mdPath)) ?? Environment.CurrentDirectory;
        return Path.GetRelativePath(mdDir, outputAbs).Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string? ResolveMarkdownLink(string markdownPath, string target)
    {
        if (
            string.IsNullOrWhiteSpace(target)
            || target.StartsWith('#')
            || Uri.TryCreate(target, UriKind.Absolute, out _)
        )
        {
            return null;
        }

        var markdownDir =
            Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? Environment.CurrentDirectory;
        return Path.GetFullPath(
            Path.Combine(
                markdownDir,
                Uri.UnescapeDataString(target).Replace('/', Path.DirectorySeparatorChar)
            )
        );
    }

    public static bool IsPathInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').Replace(Path.DirectorySeparatorChar, '/');

    public static string NormalizeFilter(string filter)
    {
        var normalized = NormalizePath(filter.Trim());
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        return normalized;
    }

    public static bool MatchesFilter(string relativePath, string filter)
    {
        var pattern = new StringBuilder("^");
        for (var i = 0; i < filter.Length; i++)
        {
            switch (filter[i])
            {
                case '*' when i + 1 < filter.Length && filter[i + 1] == '*':
                    i++;
                    if (i + 1 < filter.Length && filter[i + 1] == '/')
                    {
                        i++;
                        pattern.Append("(?:.*/)?");
                    }
                    else
                    {
                        pattern.Append(".*");
                    }
                    break;
                case '*':
                    // Filters are applied to the complete input-relative filepath.
                    // Let a single wildcard span directories so patterns such as
                    // */masking-secrets/* and */manual-*.md work at any depth.
                    pattern.Append(".*");
                    break;
                case '?':
                    pattern.Append("[^/]");
                    break;
                default:
                    pattern.Append(Regex.Escape(filter[i].ToString()));
                    break;
            }
        }
        pattern.Append('$');

        var options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }
        return Regex.IsMatch(
            NormalizePath(relativePath),
            pattern.ToString(),
            options,
            TimeSpan.FromSeconds(1)
        );
    }
}
