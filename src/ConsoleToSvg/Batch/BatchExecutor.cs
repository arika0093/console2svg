using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        var builder = new StringBuilder();
        if (!isWindows)
        {
            builder.AppendLine("set -e");
        }

        if (job.Setup.Length > 0 && setupLogPath is not null)
        {
            builder.AppendLine(
                isWindows
                    ? $"( {job.Setup} ) > \"{setupLogPath}\" 2>&1"
                    : $"{{ {job.Setup} ; }} > \"{setupLogPath}\" 2>&1"
            );
        }

        if (isWindows)
        {
            builder.AppendLine($"echo {BatchMarkdown.CaptureMarker}");
            builder.AppendLine("cls");
        }
        else
        {
            builder.AppendLine($"printf '{BatchMarkdown.CaptureMarker}\\n'");
            builder.AppendLine("printf '\\033[2J\\033[H'");
        }

        builder.Append(job.Capture);
        if (!job.Capture.EndsWith('\n'))
        {
            builder.AppendLine();
        }

        return builder.ToString();
    }

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

        var baseTime = session.Events[index].Time;
        var kept = session
            .Events.Skip(index + 1)
            .Select(e => new AsciicastEvent
            {
                Time = Math.Max(0, e.Time - baseTime),
                Type = e.Type,
                Data = e.Data,
            })
            .ToList();
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

    /// <summary>Resolves a marker -o value under the assets directory.</summary>
    public static string? ResolveOutput(string assetsDir, string outputRelative)
    {
        var combined = Path.GetFullPath(
            Path.Combine(assetsDir, outputRelative.Replace('/', Path.DirectorySeparatorChar))
        );
        var root = assetsDir.EndsWith(Path.DirectorySeparatorChar)
            ? assetsDir
            : assetsDir + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return combined;
    }

    /// <summary>Markdown-relative link target for a generated asset.</summary>
    public static string Relativize(string mdPath, string outputAbs)
    {
        var mdDir = Path.GetDirectoryName(Path.GetFullPath(mdPath)) ?? Environment.CurrentDirectory;
        return Path.GetRelativePath(mdDir, outputAbs).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>True when the sidecar hash matches (dev fast path).</summary>
    public static async Task<bool> IsCacheHitAsync(
        string outputAbs,
        string hash,
        CancellationToken ct
    )
    {
        var sidecar = outputAbs + ".sha256";
        if (!File.Exists(outputAbs) || !File.Exists(sidecar))
        {
            return false;
        }

        var saved = await File.ReadAllTextAsync(sidecar, ct).ConfigureAwait(false);
        return string.Equals(saved.Trim(), hash, StringComparison.OrdinalIgnoreCase);
    }
}
