using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Resolves directories that may hold binaries/native libraries bundled
/// alongside the console2svg executable.
/// </summary>
/// <remarks>
/// Package managers such as WinGet run "portable" zip installs through a
/// symlink placed in a shared Links folder. On Windows, <see
/// cref="Environment.ProcessPath"/> and <see cref="AppContext.BaseDirectory"/>
/// then report the symlink's directory rather than the real install
/// directory the bundled ffmpeg/rsvg/resvg assets live in, so lookups next to
/// the executable silently fail (see console2svg#129 / winget-cli#2711).
/// Resolving the symlink's final target recovers the real directory.
/// </remarks>
public static class AppPaths
{
    /// <summary>
    /// Returns directories to probe for bundled assets, most likely first.
    /// </summary>
    public static IReadOnlyList<string> GetBundledAssetDirectories()
    {
        var candidates = new List<string>();

        void AddCandidate(string? dir)
        {
            if (
                !string.IsNullOrEmpty(dir)
                && !candidates.Contains(dir, StringComparer.OrdinalIgnoreCase)
            )
            {
                candidates.Add(dir);
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            AddCandidate(Path.GetDirectoryName(processPath));
            AddCandidate(Path.GetDirectoryName(ResolveFinalTarget(processPath)));
        }

        AddCandidate(AppContext.BaseDirectory);

        return candidates;
    }

    /// <summary>
    /// Follows filesystem reparse points (symlinks/junctions) to their final
    /// target. Returns <paramref name="path"/> unchanged when it isn't a
    /// reparse point or resolution fails for any reason.
    /// </summary>
    private static string ResolveFinalTarget(string path)
    {
        try
        {
            var final = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return final?.FullName ?? path;
        }
        catch
        {
            return path;
        }
    }
}
