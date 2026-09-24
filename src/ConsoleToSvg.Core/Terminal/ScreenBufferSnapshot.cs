using System;

namespace ConsoleToSvg.Terminal;

/// <summary>A versioned snapshot of the visible terminal viewport.</summary>
public sealed class ScreenBufferSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public int Width { get; init; }
    public int Height { get; init; }
    public int CursorRow { get; init; }
    public int CursorColumn { get; init; }
    public bool CursorVisible { get; init; }
    public bool IsAlternateScreen { get; init; }
    public ScreenCellSnapshot[] Cells { get; init; } = [];
}

/// <summary>Serializable cell state used by <see cref="ScreenBufferSnapshot"/>.</summary>
public sealed class ScreenCellSnapshot
{
    public string Text { get; init; } = " ";
    public string Foreground { get; init; } = string.Empty;
    public string Background { get; init; } = string.Empty;
    public string? UnderlineColor { get; init; }
    public string? Hyperlink { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Reversed { get; init; }
    public bool Faint { get; init; }
    public bool Hidden { get; init; }
    public bool Strikethrough { get; init; }
    public bool Overline { get; init; }
    public bool Blink { get; init; }
    public bool IsWide { get; init; }
    public bool IsWideContinuation { get; init; }
}
