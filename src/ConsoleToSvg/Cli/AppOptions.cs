using System;
using System.Collections.Generic;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Cli;

public enum OutputMode
{
    Image,
    Video,
    Repeat,
}

public sealed class AppOptions
{
    public bool Verbose { get; set; }

    public string? VerboseLogPath { get; set; }

    public bool ShowVersion { get; set; }

    public string? Command { get; set; }

    /// <summary>
    /// Unmodified arguments following <c>--</c>. Interactive mode uses these to
    /// start the requested program without losing argument boundaries.
    /// </summary>
    public string[]? DelimitedCommand { get; set; }

    public string? InputCastPath { get; set; }

    public string OutputPath { get; set; } = "output.svg";

    public OutputMode Mode { get; set; } = OutputMode.Image;

    /// <summary>Patterns to mask in output (replaced with asterisks).</summary>
    public List<string> MaskPatterns { get; } = [];

    /// <summary>True when --mode (or -v) was explicitly supplied on the command line.</summary>
    public bool IsModeExplicit { get; set; }

    public int? Width { get; set; } = null;

    public bool WidthAdjust { get; set; } = true;

    public int? Height { get; set; } = null;

    public bool HeightAdjust { get; set; } = true;

    public int? Frame { get; set; }

    /// <summary>Single time point in seconds (mutually exclusive with --frame).</summary>
    public double? Time { get; set; }

    /// <summary>Range start in seconds (used with --time 1.5-3.0).</summary>
    public double? TimeStart { get; set; }

    /// <summary>Range end in seconds (used with --time 1.5-3.0).</summary>
    public double? TimeEnd { get; set; }

    public string CropTop { get; set; } = "0";

    public string CropRight { get; set; } = "0";

    public string CropBottom { get; set; } = "0";

    public string CropLeft { get; set; } = "0";

    public string Theme { get; set; } = "dark";

    public string? ForeColor { get; set; }

    public string? SaveCastPath { get; set; }

    public string? ReplaySavePath { get; set; }

    public string? ReplayPath { get; set; }

    public bool NoColorEnv { get; set; }

    public bool NoDeleteEnvs { get; set; }

    public string? Font { get; set; }

    public double? FontSize { get; set; } = null;

    public string Window { get; set; } = "none";

    public double? Padding { get; set; }

    public bool Loop { get; set; } = true;

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 0d;

    public double VideoFadeOut { get; set; } = 0d;

    public VideoTimingMode VideoTiming { get; set; } = VideoTimingMode.Deterministic;

    public double OutputCoalesceMs { get; set; } = 0d;

    public double Opacity { get; set; } = 1d;

    public bool WithCommand { get; set; }

    public string? Prompt { get; set; }

    public string? Header { get; set; }

    public string LengthAdjust { get; set; } = "spacing";

    public System.Collections.Generic.List<string> Background { get; set; } = [];

    public double? Timeout { get; set; } = null;

    /// <summary>Enable PC (desktop) mode for the selected window style.</summary>
    public bool PcMode { get; set; }

    /// <summary>Override the desktop padding value when PC mode is active.</summary>
    public double? PcPadding { get; set; }

    /// <summary>Override the terminal's own background color (e.g. "#0c0c0c").</summary>
    public string? BackColor { get; set; }

    /// <summary>Write SVG output to stdout instead of a file. PTY forwarding is suppressed.</summary>
    public bool StdOut { get; set; }

    /// <summary>Run the user's shell in a PTY and capture the terminal on demand.</summary>
    public bool Interactive { get; set; }

    /// <summary>Directory path to save individual static SVG frames (one per visual frame).</summary>
    public string? SaveFramesDir { get; set; }

    /// <summary>Target output image width in pixels. null = auto (derived from content).</summary>
    public double? SizeWidth { get; set; }

    /// <summary>Target output image height in pixels. null = auto (derived from content).</summary>
    public double? SizeHeight { get; set; }

    /// <summary>
    /// Which SVG → raster converter to use. Default: <see cref="SvgConverterMode.Auto"/>,
    /// which prefers the bundled resvg host, then ffmpeg+librsvg.
    /// </summary>
    public SvgConverterMode SvgConverter { get; set; } = SvgConverterMode.Auto;
}
