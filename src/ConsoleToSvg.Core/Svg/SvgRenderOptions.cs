namespace ConsoleToSvg.Svg;

public sealed class SvgRenderOptions
{
    public string Theme { get; set; } = "dark";

    public CropOptions Crop { get; set; } = CropOptions.Parse("0", "0", "0", "0");

    public int? Frame { get; set; }

    /// <summary>Single time point in seconds; converted to frame index internally.</summary>
    public double? Time { get; set; }

    /// <summary>Range start in seconds for filtering frames in video/save-frames mode.</summary>
    public double? TimeStart { get; set; }

    /// <summary>Range end in seconds for filtering frames in video/save-frames mode.</summary>
    public double? TimeEnd { get; set; }

    public string? Font { get; set; }

    public double FontSize { get; set; } = 14d;

    /// <summary>Window chrome definition. null = no chrome (transparent/plain).</summary>
    public ChromeDefinition? Chrome { get; set; }

    public double Padding { get; set; }

    public bool Loop { get; set; }

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 1d;

    public double VideoFadeOut { get; set; } = 0d;

    public VideoTimingMode VideoTiming { get; set; } = VideoTimingMode.Deterministic;

    public double Opacity { get; set; } = 1d;

    public int? HeightRows { get; set; }

    public string? CommandHeader { get; set; }

    public string? ForeColor { get; set; }

    public string LengthAdjust { get; set; } = "spacing";

    /// <summary>Background specification: null = default, 1 element = solid color or image path, 2 elements = gradient.</summary>
    public string[]? Background { get; set; }

    /// <summary>Override the terminal's own background color. null = use theme default.</summary>
    public string? BackColor { get; set; }

    /// <summary>Target output image width in pixels. null = auto (derived from content).</summary>
    public double? SizeWidth { get; set; }

    /// <summary>Target output image height in pixels. null = auto (derived from content).</summary>
    public double? SizeHeight { get; set; }

    /// <summary>Patterns to mask in output (replaced with asterisks).</summary>
    public string[]? MaskPatterns { get; init; }

}
