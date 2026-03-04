using System;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal static class SvgRenderShared
{
    internal static Theme ResolveTheme(SvgRenderOptions options)
    {
        var theme = Theme.Resolve(options.Theme);
        if (options.Chrome?.ThemeBackgroundOverride is string bgOverride)
        {
            theme = theme.WithBackground(bgOverride);
        }
        if (!string.IsNullOrWhiteSpace(options.BackColor))
        {
            theme = theme.WithBackground(options.BackColor);
        }
        if (!string.IsNullOrWhiteSpace(options.ForeColor))
        {
            theme = theme.WithForeground(options.ForeColor);
        }

        return theme;
    }

    internal static SvgDocumentBuilder.Context CreateContext(
        ScreenBuffer buffer,
        SvgRenderOptions options,
        bool includeScrollback,
        int commandHeaderRows
    )
    {
        return SvgDocumentBuilder.CreateContext(
            buffer,
            options.Crop,
            includeScrollback,
            options.Chrome,
            options.Padding,
            options.HeightRows,
            commandHeaderRows,
            options.FontSize
        );
    }

    internal static bool HasTrailingBlankIndicators(RecordingSession session, int startIndex)
    {
        var start = Math.Max(0, startIndex);
        for (var i = start; i < session.Events.Count; i++)
        {
            var data = session.Events[i].Data;
            if (ContainsAlternateScreenLeave(data) || ContainsLikelyScreenClear(data))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsBlankFrame(ScreenBuffer buffer)
    {
        for (var row = 0; row < buffer.Height; row++)
        {
            for (var col = 0; col < buffer.Width; col++)
            {
                var cell = buffer.GetCell(row, col);
                if (cell.Text != " " || cell.IsWide || cell.IsWideContinuation)
                {
                    return false;
                }

                if (
                    !string.Equals(
                        cell.Foreground,
                        buffer.DefaultStyle.Foreground,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        cell.Background,
                        buffer.DefaultStyle.Background,
                        StringComparison.Ordinal
                    )
                    || cell.Bold
                    || cell.Italic
                    || cell.Underline
                    || cell.Reversed
                    || cell.Faint
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ContainsAlternateScreenLeave(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        return data.Contains("\u001b[?1049l", StringComparison.Ordinal)
            || data.Contains("\u001b[?47l", StringComparison.Ordinal)
            || data.Contains("\u001b[?1047l", StringComparison.Ordinal);
    }

    private static bool ContainsLikelyScreenClear(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return false;
        }

        return data.Contains("\u001b[J", StringComparison.Ordinal)
            || data.Contains("\u001b[2J", StringComparison.Ordinal)
            || data.Contains("\u001b[3J", StringComparison.Ordinal)
            || data.Contains("\u001b[H", StringComparison.Ordinal)
            || data.Contains("\u001b[;H", StringComparison.Ordinal);
    }
}
