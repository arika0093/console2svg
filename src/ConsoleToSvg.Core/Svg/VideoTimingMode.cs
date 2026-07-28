namespace ConsoleToSvg.Svg;

/// <summary>Controls whether animated SVG timestamps preserve recorded timing.</summary>
public enum VideoTimingMode
{
    /// <summary>Normalize frame times to produce reproducible output.</summary>
    Deterministic,

    /// <summary>Preserve measured event timing.</summary>
    Realtime,
}
