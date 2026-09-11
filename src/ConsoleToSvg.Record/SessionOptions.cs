using System.Collections.Generic;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Persistable settings needed to reproduce a terminal session.
/// This is intentionally independent from the runtime CLI <c>AppOptions</c>
/// model so it can also be used by project configuration.
/// </summary>
public sealed class SessionOptions
{
    public TerminalSessionOptions Terminal { get; set; } = new();

    public AppearanceSessionOptions Appearance { get; set; } = new();

    public RenderSessionOptions Render { get; set; } = new();

    public SessionOptions DeepClone() =>
        new()
        {
            Terminal = Terminal.DeepClone(),
            Appearance = Appearance.DeepClone(),
            Render = Render.DeepClone(),
        };
}

public sealed class TerminalSessionOptions
{
    public int? Width { get; set; }

    public int? Height { get; set; }

    internal TerminalSessionOptions DeepClone() => new() { Width = Width, Height = Height };
}

public sealed class AppearanceSessionOptions
{
    public string? Theme { get; set; }

    public string? Font { get; set; }

    public double? FontSize { get; set; }

    public string? Window { get; set; }

    public double? Margin { get; set; }

    public double? Padding { get; set; }

    public string? ForeColor { get; set; }

    public string? BackColor { get; set; }

    public List<string>? Background { get; set; }

    internal AppearanceSessionOptions DeepClone() =>
        new()
        {
            Theme = Theme,
            Font = Font,
            FontSize = FontSize,
            Window = Window,
            Margin = Margin,
            Padding = Padding,
            ForeColor = ForeColor,
            BackColor = BackColor,
            Background = Background is null ? null : [.. Background],
        };
}

public sealed class RenderSessionOptions
{
    /// <summary><c>image</c> or <c>video</c>.</summary>
    public string? Mode { get; set; }

    public double? Fps { get; set; }

    /// <summary><c>deterministic</c> or <c>realtime</c>.</summary>
    public string? Timing { get; set; }

    public bool? Loop { get; set; }

    public double? Sleep { get; set; }

    public double? FadeOut { get; set; }

    public double? OutputCoalesceMs { get; set; }

    internal RenderSessionOptions DeepClone() =>
        new()
        {
            Mode = Mode,
            Fps = Fps,
            Timing = Timing,
            Loop = Loop,
            Sleep = Sleep,
            FadeOut = FadeOut,
            OutputCoalesceMs = OutputCoalesceMs,
        };
}
