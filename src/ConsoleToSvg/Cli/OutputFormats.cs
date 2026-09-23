using System;
using System.IO;

namespace ConsoleToSvg.Cli;

/// <summary>
/// Helpers for the shared <c>--format</c> option. The option selects the output
/// container (svg, png, gif, mp4, ...) independently of the <c>-o</c> file
/// extension. Batch markers can therefore request a non-SVG format without
/// inventing a synthetic output name.
/// </summary>
public static class OutputFormats
{
    /// <summary>Formats accepted by <c>--format</c>, in help/completion order.</summary>
    public static readonly string[] Supported =
    [
        "svg",
        "png",
        "jpg",
        "jpeg",
        "webp",
        "gif",
        "mp4",
        "webm",
    ];

    /// <summary>Normalizes a user supplied format to a lowercase extension without a dot.</summary>
    public static string Normalize(string value)
    {
        var normalized = value.Trim().TrimStart('.').ToLowerInvariant();
        return normalized == "jpeg" ? "jpg" : normalized;
    }

    /// <summary>
    /// Rewrites the extension of <paramref name="outputPath"/> to the requested format.
    /// Returns the path unchanged when no format was supplied.
    /// </summary>
    public static string ApplyFormat(string outputPath, string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return outputPath;
        }

        var directory = Path.GetDirectoryName(outputPath);
        var name = Path.GetFileNameWithoutExtension(outputPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "output";
        }

        var fileName = $"{name}.{format}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}
