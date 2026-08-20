using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal static partial class SvgDocumentBuilder
{
    public static void AppendFrameGroup(
        StringBuilder sb,
        ScreenBuffer buffer,
        Context context,
        Theme theme,
        string? id,
        string? @class,
        bool includeScrollback = false,
        double opacity = 1d,
        string lengthAdjust = "spacing"
    )
    {
        var effectiveLengthAdjust = string.IsNullOrWhiteSpace(lengthAdjust)
            ? "spacing"
            : lengthAdjust;
        sb.Append("<g");
        if (!string.IsNullOrWhiteSpace(id))
        {
            sb.Append(" id=\"");
            sb.Append(EscapeAttribute(id));
            sb.Append("\"");
        }

        if (!string.IsNullOrWhiteSpace(@class))
        {
            sb.Append(" class=\"");
            sb.Append(EscapeAttribute(@class));
            sb.Append("\"");
        }

        sb.Append(" transform=\"translate(");
        sb.Append(Format(context.ContentOffsetX - context.PixelCropLeft));
        sb.Append(' ');
        sb.Append(Format(context.ContentOffsetY - context.PixelCropTop));
        sb.Append(")\">\n");

        sb.Append("<rect width=\"");
        sb.Append(Format(context.ContentWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ContentHeight));
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");

        // Collect box drawing segments for merging
        var hSegments = new List<(double Y, double StartX, double EndX, string Color, double StrokeWidth)>();
        var vSegments = new List<(double X, double StartY, double EndY, string Color, double StrokeWidth)>();

        for (var row = context.StartRow; row < context.EndRowExclusive; row++)
        {
            var y = (row - context.StartRow) * context.CellHeight;

            // --- Background pass: merge consecutive cells of the same bg color ---
            var bgRunStart = context.StartCol;
            string? bgRunColor = null;
            for (var col = context.StartCol; col <= context.EndColExclusive; col++)
            {
                string? cellBg = null;
                if (col < context.EndColExclusive)
                {
                    var c = includeScrollback
                        ? buffer.GetCellFromTop(row, col)
                        : buffer.GetCell(row, col);
                    var eBg = c.Reversed ? c.Foreground : c.Background;
                    if (!string.Equals(eBg, theme.Background, StringComparison.OrdinalIgnoreCase))
                    {
                        cellBg = eBg;
                    }
                }

                if (
                    cellBg != null
                    && string.Equals(cellBg, bgRunColor, StringComparison.OrdinalIgnoreCase)
                )
                {
                    // extend current run
                    continue;
                }

                // flush previous run
                if (bgRunColor != null && col > bgRunStart)
                {
                    var rx = (bgRunStart - context.StartCol) * context.CellWidth;
                    var rw = (col - bgRunStart) * context.CellWidth;
                    sb.Append("<rect class=\"bg\" x=\"");
                    sb.Append(Format(rx));
                    sb.Append("\" y=\"");
                    sb.Append(Format(y));
                    sb.Append("\" width=\"");
                    sb.Append(Format(rw));
                    sb.Append("\" height=\"");
                    sb.Append(Format(context.CellHeight));
                    sb.Append("\" fill=\"");
                    sb.Append(bgRunColor);
                    sb.Append("\"/>\n");
                }

                bgRunColor = cellBg;
                bgRunStart = col;
            }

            // --- Foreground pass: group consecutive cells with identical style ---
            var fgRunStart = context.StartCol;
            var fgRunText = new StringBuilder();
            string? fgRunColor = null;
            bool fgBold = false,
                fgItalic = false,
                fgUnderline = false,
                fgStrikethrough = false,
                fgOverline = false,
                fgBlink = false;
            string? fgUnderlineColor = null;
            int fgRunCellCount = 0;

            void FlushFgRun()
            {
                if (fgRunCellCount == 0 || fgRunColor == null)
                {
                    return;
                }

                var tx = (fgRunStart - context.StartCol) * context.CellWidth;
                var tLen = fgRunCellCount * context.CellWidth;
                sb.Append("<text class=\"crt");
                if (fgBlink)
                {
                    sb.Append(" blink");
                }
                sb.Append("\" x=\"");
                sb.Append(Format(tx));
                sb.Append("\" y=\"");
                sb.Append(Format(y + context.BaselineOffset));
                sb.Append("\" fill=\"");
                sb.Append(fgRunColor);
                sb.Append("\" textLength=\"");
                sb.Append(Format(tLen));
                sb.Append("\" lengthAdjust=\"");
                sb.Append(EscapeAttribute(effectiveLengthAdjust));
                sb.Append("\"");
                if (
                    fgBold
                    || fgItalic
                    || fgUnderline
                    || fgStrikethrough
                    || fgOverline
                    || fgUnderlineColor != null
                )
                {
                    sb.Append(" style=\"");
                    if (fgBold)
                        sb.Append("font-weight:bold;");
                    if (fgItalic)
                        sb.Append("font-style:italic;");
                    if (fgUnderline || fgStrikethrough || fgOverline)
                    {
                        sb.Append("text-decoration:");
                        if (fgUnderline)
                            sb.Append("underline ");
                        if (fgStrikethrough)
                            sb.Append("line-through ");
                        if (fgOverline)
                            sb.Append("overline ");
                        sb.Append(';');
                    }
                    if (fgUnderlineColor != null)
                    {
                        sb.Append("text-decoration-color:");
                        sb.Append(fgUnderlineColor);
                        sb.Append(';');
                    }
                    sb.Append("\"");
                }
                sb.Append('>');
                sb.Append(fgRunText);
                sb.Append("</text>\n");
                fgRunText.Clear();
                fgRunCellCount = 0;
                fgRunColor = null;
            }

            for (var col = context.StartCol; col < context.EndColExclusive; col++)
            {
                var cell = includeScrollback
                    ? buffer.GetCellFromTop(row, col)
                    : buffer.GetCell(row, col);

                if (cell.IsWideContinuation)
                {
                    continue;
                }

                if (cell.Text == " ")
                {
                    // Space: flush current run and skip (background already drawn)
                    FlushFgRun();
                    fgRunStart = col + 1;
                    continue;
                }

                if (cell.Hidden)
                {
                    FlushFgRun();
                    fgRunStart = col + 1;
                    continue;
                }

                var effectiveFg = cell.Reversed ? cell.Background : cell.Foreground;
                effectiveFg = ApplyIntensity(effectiveFg, cell.Bold, cell.Faint);

                var cellX = (col - context.StartCol) * context.CellWidth;
                var cellW = cell.IsWide ? context.CellWidth * 2d : context.CellWidth;

                // Unicode Block Elements (U+2580–U+259F): render as calibrated rects so that
                // adjacent cells always tile seamlessly regardless of font metrics.
                if (IsBlockElement(cell.Text))
                {
                    FlushFgRun();
                    RenderBlockElement(
                        sb,
                        cell.Text,
                        cellX,
                        y,
                        cellW,
                        context.CellHeight,
                        effectiveFg
                    );
                    fgRunStart = col + (cell.IsWide ? 2 : 1);
                    continue;
                }

                if (IsSingleLineBoxDrawing(cell.Text))
                {
                    FlushFgRun();
                    // Collect segments instead of rendering immediately
                    var character = cell.Text[0];
                    var centerX = cellX + cellW / 2d;
                    var centerY = y + context.CellHeight / 2d;
                    var sw = context.FontSize / 14d;

                    // Determine which directions this character connects to
                    var left =
                        character
                        is '\u2500'
                            or '\u2510'
                            or '\u2518'
                            or '\u2524'
                            or '\u252C'
                            or '\u2534'
                            or '\u253C';
                    var right =
                        character
                        is '\u2500'
                            or '\u250C'
                            or '\u2514'
                            or '\u251C'
                            or '\u252C'
                            or '\u2534'
                            or '\u253C';
                    var up =
                        character
                        is '\u2502'
                            or '\u2514'
                            or '\u2518'
                            or '\u251C'
                            or '\u2524'
                            or '\u2534'
                            or '\u253C';
                    var down =
                        character
                        is '\u2502'
                            or '\u250C'
                            or '\u2510'
                            or '\u251C'
                            or '\u2524'
                            or '\u252C'
                            or '\u253C';

                    if (left)
                        hSegments.Add((centerY, cellX, centerX, effectiveFg, sw));
                    if (right)
                        hSegments.Add((centerY, centerX, cellX + cellW, effectiveFg, sw));
                    if (up)
                        vSegments.Add((centerX, y, centerY, effectiveFg, sw));
                    if (down)
                        vSegments.Add((centerX, centerY, y + context.CellHeight, effectiveFg, sw));

                    fgRunStart = col + 1;
                    continue;
                }

                var sameStyle =
                    string.Equals(effectiveFg, fgRunColor, StringComparison.OrdinalIgnoreCase)
                    && cell.Bold == fgBold
                    && cell.Italic == fgItalic
                    && cell.Underline == fgUnderline
                    && cell.Strikethrough == fgStrikethrough
                    && cell.Overline == fgOverline
                    && cell.Blink == fgBlink
                    && string.Equals(
                        cell.UnderlineColor,
                        fgUnderlineColor,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !cell.IsWide;

                if (!sameStyle)
                {
                    FlushFgRun();
                    fgRunStart = col;
                    fgRunColor = effectiveFg;
                    fgBold = cell.Bold;
                    fgItalic = cell.Italic;
                    fgUnderline = cell.Underline;
                    fgStrikethrough = cell.Strikethrough;
                    fgOverline = cell.Overline;
                    fgBlink = cell.Blink;
                    fgUnderlineColor = cell.UnderlineColor;
                }

                fgRunText.Append(EscapeText(cell.Text));
                fgRunCellCount += cell.IsWide ? 2 : 1;

                // Wide chars must always be emitted immediately so the next char
                // starts its own run at the correct x-offset.
                if (cell.IsWide)
                {
                    FlushFgRun();
                    fgRunStart = col + 1; // col+1 is IsWideContinuation, next real col is col+2
                }
            }

            FlushFgRun();
        }

        // Render merged box drawing segments
        RenderMergedBoxSegments(sb, hSegments, vSegments, context.CellWidth, context.CellHeight);

        sb.Append("</g>\n");
    }

    public static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsBlockElement(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int cp;
        if (text.Length == 1)
        {
            cp = text[0];
        }
        else if (char.IsHighSurrogate(text[0]) && text.Length >= 2)
        {
            cp = char.ConvertToUtf32(text[0], text[1]);
        }
        else
        {
            cp = -1;
        }

        // Unicode Block Elements (U+2580–U+259F), excluding shade chars (U+2591–U+2593)
        return cp is >= 0x2580 and <= 0x259F and not (0x2591 or 0x2592 or 0x2593);
    }

    private static bool IsSingleLineBoxDrawing(string text) =>
        text.Length == 1
        && text[0]
            is '\u2500'
                or '\u2502'
                or '\u250C'
                or '\u2510'
                or '\u2514'
                or '\u2518'
                or '\u251C'
                or '\u2524'
                or '\u252C'
                or '\u2534'
                or '\u253C';

    private static void RenderMergedBoxSegments(
        StringBuilder sb,
        List<(double Y, double StartX, double EndX, string Color, double StrokeWidth)> hSegments,
        List<(double X, double StartY, double EndY, string Color, double StrokeWidth)> vSegments,
        double cellWidth,
        double cellHeight
    )
    {
        if (hSegments.Count == 0 && vSegments.Count == 0)
        {
            return;
        }

        var mergedRects = new List<(double X, double Y, double Width, double Height, string Color)>();
        // Use a tolerance relative to cell size to avoid merging across real gaps at tiny font sizes
        var mergeTolerance = Math.Min(cellWidth, cellHeight) * 0.01;

        static void MergeAxis<T>(
            List<T> segments,
            Func<T, double> getPosition,
            Func<T, double> getStart,
            Func<T, double> getEnd,
            Func<T, string> getColor,
            Func<T, double> getStrokeWidth,
            Func<double, double, double, double, string, (double X, double Y, double Width, double Height, string Color)> toRect,
            List<(double X, double Y, double Width, double Height, string Color)> output,
            double tolerance)
        {
            var groups = segments.GroupBy(s =>
                (Position: Math.Round(getPosition(s), 3), Color: getColor(s), StrokeWidth: Math.Round(getStrokeWidth(s), 3))
            );
            foreach (var group in groups)
            {
                var sorted = group.OrderBy(s => getStart(s)).ToList();
                double currentStart = getStart(sorted[0]);
                double currentEnd = getEnd(sorted[0]);
                double sw = getStrokeWidth(sorted[0]);
                string color = group.Key.Color;

                for (int i = 1; i < sorted.Count; i++)
                {
                    if (getStart(sorted[i]) <= currentEnd + tolerance)
                    {
                        currentEnd = Math.Max(currentEnd, getEnd(sorted[i]));
                    }
                    else
                    {
                        output.Add(toRect(group.Key.Position, currentStart, currentEnd, sw, color));
                        currentStart = getStart(sorted[i]);
                        currentEnd = getEnd(sorted[i]);
                    }
                }
                output.Add(toRect(group.Key.Position, currentStart, currentEnd, sw, color));
            }
        }

        MergeAxis(
            hSegments,
            s => s.Y, s => s.StartX, s => s.EndX, s => s.Color, s => s.StrokeWidth,
            (y, start, end, sw, color) => (start, y - sw / 2d, end - start, sw, color),
            mergedRects,
            mergeTolerance);

        MergeAxis(
            vSegments,
            s => s.X, s => s.StartY, s => s.EndY, s => s.Color, s => s.StrokeWidth,
            (x, start, end, sw, color) => (x - sw / 2d, start, sw, end - start, color),
            mergedRects,
            mergeTolerance);

        // Render all merged rectangles, grouped by color
        var colorGroups = mergedRects.GroupBy(r => r.Color);
        foreach (var group in colorGroups)
        {
            sb.Append("<path class=\"box\" d=\"");
            foreach (var rect in group)
            {
                sb.Append('M');
                sb.Append(Format(rect.X));
                sb.Append(' ');
                sb.Append(Format(rect.Y));
                sb.Append('H');
                sb.Append(Format(rect.X + rect.Width));
                sb.Append('V');
                sb.Append(Format(rect.Y + rect.Height));
                sb.Append('H');
                sb.Append(Format(rect.X));
                sb.Append('Z');
            }
            sb.Append("\" fill=\"");
            sb.Append(group.Key);
            sb.Append("\"/>\n");
        }
    }

    private static void RenderBlockElement(
        StringBuilder sb,
        string text,
        double x,
        double y,
        double cellRectWidth,
        double cellRectHeight,
        string fill
    )
    {
        var cp = text.Length == 1 ? text[0] : char.ConvertToUtf32(text[0], text[1]);

        var w = cellRectWidth;
        var h = cellRectHeight;
        var hh = h / 2d;
        var hw = w / 2d;

        switch (cp)
        {
            case 0x2580:
                R(x, y, w, hh);
                break; // ▀ Upper half
            case 0x2581:
                R(x, y + h * 7d / 8, w, h / 8d);
                break; // ▁ELower 1/8
            case 0x2582:
                R(x, y + h * 3d / 4, w, h / 4d);
                break; // ▁ELower 1/4
            case 0x2583:
                R(x, y + h * 5d / 8, w, h * 3d / 8);
                break; // ▁ELower 3/8
            case 0x2584:
                R(x, y + hh, w, hh);
                break; // ▁ELower half
            case 0x2585:
                R(x, y + h * 3d / 8, w, h * 5d / 8);
                break; // ▁ELower 5/8
            case 0x2586:
                R(x, y + h / 4d, w, h * 3d / 4);
                break; // ▁ELower 3/4
            case 0x2587:
                R(x, y + h / 8d, w, h * 7d / 8);
                break; // ▁ELower 7/8
            case 0x2588:
                R(x, y, w, h);
                break; // ▁EFull block
            case 0x2589:
                R(x, y, w * 7d / 8, h);
                break; // ▁ELeft 7/8
            case 0x258A:
                R(x, y, w * 3d / 4, h);
                break; // ▁ELeft 3/4
            case 0x258B:
                R(x, y, w * 5d / 8, h);
                break; // ▁ELeft 5/8
            case 0x258C:
                R(x, y, hw, h);
                break; // ▁ELeft half
            case 0x258D:
                R(x, y, w * 3d / 8, h);
                break; // ▁ELeft 3/8
            case 0x258E:
                R(x, y, w / 4d, h);
                break; // ▁ELeft 1/4
            case 0x258F:
                R(x, y, w / 8d, h);
                break; // ▁ELeft 1/8
            case 0x2590:
                R(x + hw, y, hw, h);
                break; // ▁ERight half
            // 0x2591 Ex2593: shade chars handled by font (IsBlockElement returns false)
            case 0x2594:
                R(x, y, w, h / 8d);
                break; // ▁EUpper 1/8
            case 0x2595:
                R(x + w * 7d / 8, y, w / 8d, h);
                break; // ▁ERight 1/8
            case 0x2596:
                R(x, y + hh, hw, hh);
                break; // ▁EQuad lower-left
            case 0x2597:
                R(x + hw, y + hh, hw, hh);
                break; // ▁EQuad lower-right
            case 0x2598:
                R(x, y, hw, hh);
                break; // ▁EQuad upper-left
            case 0x2599:
                R(x, y, hw, hh);
                R(x, y + hh, w, hh);
                break; // ▁E
            case 0x259A:
                R(x, y, hw, hh);
                R(x + hw, y + hh, hw, hh);
                break; // ▁E
            case 0x259B:
                R(x, y, w, hh);
                R(x, y + hh, hw, hh);
                break; // ▁E
            case 0x259C:
                R(x, y, w, hh);
                R(x + hw, y + hh, hw, hh);
                break; // ▁E
            case 0x259D:
                R(x + hw, y, hw, hh);
                break; // ▁EQuad upper-right
            case 0x259E:
                R(x + hw, y, hw, hh);
                R(x, y + hh, hw, hh);
                break; // ▁E
            case 0x259F:
                R(x + hw, y, hw, hh);
                R(x, y + hh, w, hh);
                break; // ▁E
        }

        void R(double rx, double ry, double rw, double rh)
        {
            sb.Append("<rect class=\"bg\" x=\"");
            sb.Append(Format(rx));
            sb.Append("\" y=\"");
            sb.Append(Format(ry));
            sb.Append("\" width=\"");
            sb.Append(Format(rw));
            sb.Append("\" height=\"");
            sb.Append(Format(rh));
            sb.Append("\" fill=\"");
            sb.Append(fill);
            sb.Append("\"/>\n");
        }
    }

    private static string EscapeText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string EscapeAttribute(string value)
    {
        return EscapeText(value);
    }

    private static string ApplyIntensity(string color, bool bold, bool faint)
    {
        var factor = 1d;
        if (bold)
        {
            factor *= 1.2d;
        }

        if (faint)
        {
            factor *= 0.75d;
        }

        if (Math.Abs(factor - 1d) < 0.0001d)
        {
            return color;
        }

        if (!TryParseHexColor(color, out var r, out var g, out var b))
        {
            return color;
        }

        var adjustedR = Clamp((int)Math.Round(r * factor), 0, 255);
        var adjustedG = Clamp((int)Math.Round(g * factor), 0, 255);
        var adjustedB = Clamp((int)Math.Round(b * factor), 0, 255);
        return $"#{adjustedR:X2}{adjustedG:X2}{adjustedB:X2}";
    }

    private static bool TryParseHexColor(string color, out int r, out int g, out int b)
    {
        r = 0;
        g = 0;
        b = 0;
        if (string.IsNullOrWhiteSpace(color) || color.Length != 7 || color[0] != '#')
        {
            return false;
        }

        var parsedR = ParseHexByte(color[1], color[2]);
        var parsedG = ParseHexByte(color[3], color[4]);
        var parsedB = ParseHexByte(color[5], color[6]);
        if (parsedR < 0 || parsedG < 0 || parsedB < 0)
        {
            return false;
        }

        r = parsedR;
        g = parsedG;
        b = parsedB;
        return true;
    }

    private static int ParseHexByte(char high, char low)
    {
        var hi = ParseHexNibble(high);
        var lo = ParseHexNibble(low);
        if (hi < 0 || lo < 0)
        {
            return -1;
        }

        return (hi << 4) | lo;
    }

    private static int ParseHexNibble(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'A' && c <= 'F')
        {
            return c - 'A' + 10;
        }

        if (c >= 'a' && c <= 'f')
        {
            return c - 'a' + 10;
        }

        return -1;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
