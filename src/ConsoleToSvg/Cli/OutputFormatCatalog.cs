using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Cli;

public sealed record OutputFormatInfo(
    string Name,
    string Extension,
    string[] Extensions,
    bool SupportsImage,
    bool SupportsAnimation,
    bool DefaultsToAnimation = false
);

public static class OutputFormatCatalog
{
    public static IReadOnlyList<OutputFormatInfo> All { get; } =
    [
        new("SVG", "svg", ["svg"], SupportsImage: true, SupportsAnimation: true),
        new("JPEG", "jpg", ["jpg", "jpeg"], SupportsImage: true, SupportsAnimation: false),
        new("PNG", "png", ["png"], SupportsImage: true, SupportsAnimation: false),
        new(
            "GIF",
            "gif",
            ["gif"],
            SupportsImage: true,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "MP4",
            "mp4",
            ["mp4", "mpeg4"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "WebM",
            "webm",
            ["webm"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "AVI",
            "avi",
            ["avi"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "QuickTime",
            "mov",
            ["mov"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "Matroska",
            "mkv",
            ["mkv"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "Ogg Video",
            "ogv",
            ["ogv"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "Flash Video",
            "flv",
            ["flv"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "MPEG-TS",
            "ts",
            ["ts"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "Windows Media Video",
            "wmv",
            ["wmv"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
        new(
            "MPEG-4 Video",
            "m4v",
            ["m4v"],
            SupportsImage: false,
            SupportsAnimation: true,
            DefaultsToAnimation: true
        ),
    ];

    public static bool TryResolve(string extension, out OutputFormatInfo? format)
    {
        format = All.FirstOrDefault(candidate =>
            candidate.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
        );
        return format is not null;
    }
}
