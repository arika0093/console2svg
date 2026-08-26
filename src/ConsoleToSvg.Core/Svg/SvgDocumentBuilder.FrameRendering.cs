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
    public static void CollectTextStyles(
        ScreenBuffer buffer,
        in Context context,
        SvgStyleRegistry styles,
        bool includeScrollback = false
    )
    {
        for (var row = context.StartRow; row < context.EndRowExclusive; row++)
        {
            CollectRowTextStyles(buffer, row, context, styles, includeScrollback);
        }
    }

    public static void CollectTextStyles(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        System.Collections.Generic.IReadOnlyList<int> frameIndices,
        in Context context,
        SvgStyleRegistry styles
    )
    {
        var rowsBySignature =
            new Dictionary<ulong, List<(ScreenBuffer Buffer, int Row)>>();
        for (var framePosition = 0; framePosition < frameIndices.Count; framePosition++)
        {
            var buffer = frames[frameIndices[framePosition]].Buffer;
            for (var row = context.StartRow; row < context.EndRowExclusive; row++)
            {
                var signature = buffer.GetRowVisualSignature(row);
                if (rowsBySignature.TryGetValue(signature, out var candidates))
                {
                    var duplicate = false;
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        if (buffer.HasSameVisualRow(row, candidate.Buffer, candidate.Row))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (duplicate)
                    {
                        continue;
                    }
                }

                candidates ??= [];
                candidates.Add((buffer, row));
                rowsBySignature[signature] = candidates;
                CollectRowTextStyles(buffer, row, context, styles, includeScrollback: false);
            }
        }
    }

    private static void CollectRowTextStyles(
        ScreenBuffer buffer,
        int row,
        in Context context,
        SvgStyleRegistry styles,
        bool includeScrollback
    )
    {
        var pendingWhitespace = false;
        for (var col = context.StartCol; col < context.EndColExclusive; col++)
        {
            var cell = includeScrollback
                ? buffer.GetCellFromTop(row, col)
                : buffer.GetCell(row, col);
            if (
                cell.IsWideContinuation
                || cell.Hidden
                || IsBlockElement(cell.Text)
                || IsSingleLineBoxDrawing(cell.Text)
                || IsRoundedBoxDrawing(cell.Text)
            )
            {
                pendingWhitespace = false;
                continue;
            }
            if (cell.Text == " ")
            {
                pendingWhitespace = true;
                continue;
            }

            if (pendingWhitespace)
            {
                styles.CollectPreservedWhitespace();
                pendingWhitespace = false;
            }
            var effectiveFg = cell.Reversed ? cell.Background : cell.Foreground;
            effectiveFg = ApplyIntensity(effectiveFg, cell.Bold, cell.Faint);
            styles.CollectCellStyle(cell, effectiveFg);
        }
    }

    public static void AppendFrameGroup(
        SvgWriter sb,
        ScreenBuffer buffer,
        in Context context,
        Theme theme,
        SvgStyleRegistry styles,
        string? id,
        string? @class,
        bool includeScrollback = false,
        double opacity = 1d,
        string lengthAdjust = "spacing",
        string[]? maskPatterns = null,
        bool renderCursor = true,
        bool applyFontClass = true,
        bool applyContentTransform = true,
        SvgElementRegistry? elements = null
    )
    {
        var effectiveLengthAdjust = string.IsNullOrWhiteSpace(lengthAdjust)
            ? "spacing"
            : lengthAdjust;
        var startCol = context.StartCol;
        var cellWidth = context.CellWidth;
        var baselineOffset = context.BaselineOffset;
        sb.Append("<g");
        if (!string.IsNullOrWhiteSpace(id))
        {
            sb.Append(" id=\"");
            sb.Append(EscapeAttribute(id));
            sb.Append("\"");
        }

        if (applyFontClass || !string.IsNullOrWhiteSpace(@class))
        {
            sb.Append(" class=\"");
            if (applyFontClass)
            {
                sb.Append("c2 c");
                if (!string.IsNullOrWhiteSpace(@class))
                {
                    sb.Append(' ');
                }
            }
            if (!string.IsNullOrWhiteSpace(@class))
            {
                sb.Append(EscapeAttribute(@class));
            }
            sb.Append("\"");
        }

        if (applyContentTransform)
        {
            AppendTranslateAttribute(
                sb,
                context.ContentOffsetX - context.PixelCropLeft,
                context.ContentOffsetY - context.PixelCropTop
            );
        }
        sb.Append(">\n");

        if (elements is null)
        {
            sb.Append("<rect width=\"");
            sb.Append(context.ContentWidth);
            sb.Append("\" height=\"");
            sb.Append(context.ContentHeight);
            sb.Append("\" fill=\"");
            sb.Append(theme.Background);
            sb.Append("\"/>\n");
        }
        else
        {
            elements.AppendRect(
                sb,
                @class: null,
                x: null,
                y: null,
                context.ContentWidth,
                context.ContentHeight,
                theme.Background
            );
        }

        // Collect box drawing segments for merging
        var hSegments = new List<AxisSegment>();
        var vSegments = new List<AxisSegment>();
        var roundedCorners = new List<RoundedCorner>();
        var fgRunText = new StringBuilder(context.EndColExclusive - context.StartCol);

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
                    if (elements is null)
                    {
                        sb.Append("<rect class=\"q\"");
                        AppendPositionAttributes(sb, rx, y);
                        sb.Append(" width=\"");
                        sb.Append(rw);
                        sb.Append("\" height=\"");
                        sb.Append(context.CellHeight);
                        sb.Append("\" fill=\"");
                        sb.Append(bgRunColor);
                        sb.Append("\"/>\n");
                    }
                    else
                    {
                        elements.AppendRect(
                            sb,
                            "q",
                            rx,
                            y,
                            rw,
                            context.CellHeight,
                            bgRunColor
                        );
                    }
                }

                bgRunColor = cellBg;
                bgRunStart = col;
            }

            // --- Foreground pass: group consecutive cells with identical style ---
            var fgRunStart = context.StartCol;
            fgRunText.Clear();
            string? fgRunColor = null;
            bool fgBold = false,
                fgItalic = false,
                fgUnderline = false,
                fgStrikethrough = false,
                fgOverline = false,
                fgBlink = false;
            string? fgUnderlineColor = null;
            int fgRunCellCount = 0;
            bool fgRunHasSpace = false;
            int pendingSpaces = 0;

            void FlushFgRun()
            {
                if (fgRunCellCount == 0 || fgRunColor == null)
                {
                    return;
                }

                var tx = (fgRunStart - startCol) * cellWidth;
                var tLen = fgRunCellCount * cellWidth;
                var textClass = styles.GetTextClass(
                    fgRunColor,
                    fgBold,
                    fgItalic,
                    fgUnderline,
                    fgStrikethrough,
                    fgOverline,
                    fgUnderlineColor
                );
                if (fgBlink)
                {
                    textClass += " i";
                }
                if (fgRunHasSpace)
                {
                    textClass += " w";
                }

                var adjustedLength =
                    string.Equals(effectiveLengthAdjust, "spacing", StringComparison.Ordinal)
                        ? null
                        : effectiveLengthAdjust;
                sb.Append("<text class=\"");
                sb.Append(textClass);
                sb.Append("\"");
                if (tx != 0d)
                {
                    sb.Append(" x=\"");
                    sb.Append(tx);
                    sb.Append('"');
                }
                sb.Append(" y=\"");
                sb.Append(y + baselineOffset);
                sb.Append("\" textLength=\"");
                sb.Append(tLen);
                sb.Append('"');
                if (adjustedLength is not null)
                {
                    sb.Append(" lengthAdjust=\"");
                    sb.Append(EscapeAttribute(adjustedLength));
                    sb.Append("\"");
                }
                sb.Append('>');
                if (maskPatterns is null || maskPatterns.Length == 0)
                {
                    sb.Append(fgRunText);
                }
                else
                {
                    sb.Append(ApplyMask(fgRunText.ToString(), maskPatterns));
                }
                sb.Append("</text>\n");
                fgRunText.Clear();
                fgRunCellCount = 0;
                fgRunColor = null;
                fgRunHasSpace = false;
            }

            bool MatchesRunStyle(string effectiveFg, in ScreenCell cell) =>
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
                );

            for (var col = context.StartCol; col < context.EndColExclusive; col++)
            {
                var cell = includeScrollback
                    ? buffer.GetCellFromTop(row, col)
                    : buffer.GetCell(row, col);

                if (cell.IsWideContinuation)
                {
                    continue;
                }

                if (cell.Hidden)
                {
                    pendingSpaces = 0;
                    FlushFgRun();
                    fgRunStart = col + 1;
                    continue;
                }

                if (cell.Text == " ")
                {
                    // Buffer whitespace-only gaps. A space is merged into the
                    // current run only when a later non-space cell of the same
                    // style continues the run; trailing spaces (e.g. empty cells
                    // after the last character) are dropped to keep output small.
                    var spaceFg = cell.Reversed ? cell.Background : cell.Foreground;
                    spaceFg = ApplyIntensity(spaceFg, cell.Bold, cell.Faint);

                    if (!cell.Reversed && fgRunColor != null && MatchesRunStyle(spaceFg, cell))
                    {
                        pendingSpaces += 1;
                        continue;
                    }

                    pendingSpaces = 0;
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
                    pendingSpaces = 0;
                    FlushFgRun();
                    RenderBlockElement(
                        sb,
                        cell.Text,
                        cellX,
                        y,
                        cellW,
                        context.CellHeight,
                        effectiveFg,
                        elements
                    );
                    fgRunStart = col + (cell.IsWide ? 2 : 1);
                    continue;
                }

                if (IsSingleLineBoxDrawing(cell.Text))
                {
                    pendingSpaces = 0;
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
                        hSegments.Add(new AxisSegment(centerY, cellX, centerX, effectiveFg, sw));
                    if (right)
                        hSegments.Add(
                            new AxisSegment(centerY, centerX, cellX + cellW, effectiveFg, sw)
                        );
                    if (up)
                        vSegments.Add(new AxisSegment(centerX, y, centerY, effectiveFg, sw));
                    if (down)
                        vSegments.Add(
                            new AxisSegment(
                                centerX,
                                centerY,
                                y + context.CellHeight,
                                effectiveFg,
                                sw
                            )
                        );

                    fgRunStart = col + 1;
                    continue;
                }

                if (IsRoundedBoxDrawing(cell.Text))
                {
                    pendingSpaces = 0;
                    FlushFgRun();
                    roundedCorners.Add(
                        new RoundedCorner(
                            cell.Text[0],
                            cellX,
                            y,
                            cellW,
                            context.CellHeight,
                            effectiveFg,
                            context.FontSize / 14d
                        )
                    );
                    fgRunStart = col + 1;
                    continue;
                }

                var sameStyle = MatchesRunStyle(effectiveFg, cell) && !cell.IsWide;

                if (!sameStyle)
                {
                    pendingSpaces = 0;
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

                if (pendingSpaces > 0)
                {
                    fgRunText.Append(' ', pendingSpaces);
                    fgRunCellCount += pendingSpaces;
                    fgRunHasSpace = true;
                    pendingSpaces = 0;
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
        RenderMergedBoxSegments(
            sb,
            hSegments,
            vSegments,
            context.CellWidth,
            context.CellHeight,
            elements
        );
        RenderRoundedCorners(sb, roundedCorners);
        if (renderCursor)
        {
            RenderCursor(sb, buffer, context, theme, includeScrollback);
        }

        sb.Append("</g>\n");
    }

    public static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AppendTranslateAttribute(SvgWriter sb, double x, double y)
    {
        if (x == 0d && y == 0d)
        {
            return;
        }

        sb.Append(" transform=\"translate(");
        sb.Append(x);
        if (y != 0d)
        {
            sb.Append(' ');
            sb.Append(y);
        }
        sb.Append(")\"");
    }

    public static bool AppendContentTransformGroupOpen(SvgWriter sb, in Context context)
    {
        var x = context.ContentOffsetX - context.PixelCropLeft;
        var y = context.ContentOffsetY - context.PixelCropTop;
        if (x == 0d && y == 0d)
        {
            return false;
        }

        sb.Append("<g");
        AppendTranslateAttribute(sb, x, y);
        sb.Append(">\n");
        return true;
    }

    private static void AppendPositionAttributes(SvgWriter sb, double x, double y)
    {
        if (x != 0d)
        {
            sb.Append(" x=\"");
            sb.Append(x);
            sb.Append('"');
        }
        if (y != 0d)
        {
            sb.Append(" y=\"");
            sb.Append(y);
            sb.Append('"');
        }
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

    private static bool IsRoundedBoxDrawing(string text) =>
        text.Length == 1 && text[0] is >= '\u256D' and <= '\u2570';

    private static void RenderRoundedCorners(SvgWriter sb, List<RoundedCorner> corners)
    {
        foreach (var corner in corners)
        {
            var left = corner.X;
            var right = corner.X + corner.Width;
            var top = corner.Y;
            var bottom = corner.Y + corner.Height;
            var centerX = corner.X + corner.Width / 2d;
            var centerY = corner.Y + corner.Height / 2d;

            sb.Append("<path d=\"");
            switch (corner.Character)
            {
                case '\u256D': // ╭
                    AppendCurve(sb, right, centerY, centerX, centerY, centerX, bottom);
                    break;
                case '\u256E': // ╮
                    AppendCurve(sb, left, centerY, centerX, centerY, centerX, bottom);
                    break;
                case '\u256F': // ╯
                    AppendCurve(sb, left, centerY, centerX, centerY, centerX, top);
                    break;
                default: // ╰
                    AppendCurve(sb, right, centerY, centerX, centerY, centerX, top);
                    break;
            }

            sb.Append("\" fill=\"none\" stroke=\"");
            sb.Append(corner.Color);
            sb.Append("\" stroke-width=\"");
            sb.Append(corner.StrokeWidth);
            sb.Append("\"/>\n");
        }
    }

    private static void AppendCurve(
        SvgWriter sb,
        double startX,
        double startY,
        double controlX,
        double controlY,
        double endX,
        double endY
    )
    {
        sb.Append('M');
        sb.Append(startX);
        sb.Append(' ');
        sb.Append(startY);
        sb.Append('Q');
        sb.Append(controlX);
        sb.Append(' ');
        sb.Append(controlY);
        sb.Append(' ');
        sb.Append(endX);
        sb.Append(' ');
        sb.Append(endY);
    }

    private static void RenderCursor(
        SvgWriter sb,
        ScreenBuffer buffer,
        in Context context,
        Theme theme,
        bool includeScrollback
    )
    {
        if (!buffer.CursorVisible)
        {
            return;
        }

        var cursorRow = includeScrollback
            ? buffer.ScrollbackCount + buffer.CursorRow
            : buffer.CursorRow;
        if (
            cursorRow < context.StartRow
            || cursorRow >= context.EndRowExclusive
            || buffer.CursorCol < context.StartCol
            || buffer.CursorCol >= context.EndColExclusive
        )
        {
            return;
        }

        var x = (buffer.CursorCol - context.StartCol) * context.CellWidth;
        var y = (cursorRow - context.StartRow) * context.CellHeight;
        sb.Append("<rect class=\"u\"");
        AppendPositionAttributes(sb, x, y);
        sb.Append(" width=\"");
        sb.Append(context.CellWidth);
        sb.Append("\" height=\"");
        sb.Append(context.CellHeight);
        sb.Append("\" fill=\"");
        sb.Append(theme.Foreground);
        sb.Append("\" opacity=\"0.65\"/>\n");
    }

    private static void RenderMergedBoxSegments(
        SvgWriter sb,
        List<AxisSegment> hSegments,
        List<AxisSegment> vSegments,
        double cellWidth,
        double cellHeight,
        SvgElementRegistry? elements
    )
    {
        if (hSegments.Count == 0 && vSegments.Count == 0)
        {
            return;
        }

        var mergedRects = new List<BoxRect>(hSegments.Count + vSegments.Count);
        // Use a tolerance relative to cell size to avoid merging across real gaps at tiny font sizes
        var mergeTolerance = Math.Min(cellWidth, cellHeight) * 0.01;
        MergeAxis(hSegments, horizontal: true, mergeTolerance, mergedRects);
        MergeAxis(vSegments, horizontal: false, mergeTolerance, mergedRects);

        mergedRects.Sort(static (left, right) =>
            string.CompareOrdinal(left.Color, right.Color)
        );
        var pathData = elements is null ? null : new StringBuilder();
        for (var groupStart = 0; groupStart < mergedRects.Count;)
        {
            var color = mergedRects[groupStart].Color;
            pathData?.Clear();
            if (elements is null)
            {
                sb.Append("<path d=\"");
            }
            var groupEnd = groupStart;
            while (
                groupEnd < mergedRects.Count
                && string.Equals(mergedRects[groupEnd].Color, color, StringComparison.Ordinal)
            )
            {
                var rect = mergedRects[groupEnd];
                if (pathData is null)
                {
                    AppendBoxPath(sb, rect);
                }
                else
                {
                    AppendBoxPath(pathData, rect);
                }
                groupEnd++;
            }
            if (pathData is null)
            {
                sb.Append("\" fill=\"");
                sb.Append(color);
                sb.Append("\"/>\n");
            }
            else
            {
                elements!.AppendPath(sb, pathData.ToString(), color);
            }
            groupStart = groupEnd;
        }
    }

    private static void AppendBoxPath(SvgWriter sb, in BoxRect rect)
    {
        sb.Append('M');
        sb.Append(rect.X);
        sb.Append(' ');
        sb.Append(rect.Y);
        sb.Append('H');
        sb.Append(rect.X + rect.Width);
        sb.Append('V');
        sb.Append(rect.Y + rect.Height);
        sb.Append('H');
        sb.Append(rect.X);
        sb.Append('Z');
    }

    private static void AppendBoxPath(StringBuilder sb, in BoxRect rect)
    {
        AppendNumber('M', rect.X);
        AppendNumber(' ', rect.Y);
        AppendNumber('H', rect.X + rect.Width);
        AppendNumber('V', rect.Y + rect.Height);
        AppendNumber('H', rect.X);
        sb.Append('Z');

        void AppendNumber(char prefix, double value)
        {
            sb.Append(prefix);
            Span<char> buffer = stackalloc char[32];
            if (
                value.TryFormat(
                    buffer,
                    out var charsWritten,
                    "0.###",
                    CultureInfo.InvariantCulture
                )
            )
            {
                sb.Append(buffer[..charsWritten]);
            }
            else
            {
                sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }
    }

    private static void MergeAxis(
        List<AxisSegment> segments,
        bool horizontal,
        double tolerance,
        List<BoxRect> output
    )
    {
        segments.Sort(static (left, right) =>
        {
            var comparison = left.Position.CompareTo(right.Position);
            if (comparison != 0)
                return comparison;
            comparison = string.CompareOrdinal(left.Color, right.Color);
            if (comparison != 0)
                return comparison;
            comparison = left.StrokeWidth.CompareTo(right.StrokeWidth);
            return comparison != 0 ? comparison : left.Start.CompareTo(right.Start);
        });

        for (var groupStart = 0; groupStart < segments.Count;)
        {
            var first = segments[groupStart];
            var currentStart = first.Start;
            var currentEnd = first.End;
            var groupEnd = groupStart + 1;
            while (groupEnd < segments.Count && IsSameAxisGroup(first, segments[groupEnd]))
            {
                var segment = segments[groupEnd];
                if (segment.Start <= currentEnd + tolerance)
                {
                    currentEnd = Math.Max(currentEnd, segment.End);
                }
                else
                {
                    AddRect(first, currentStart, currentEnd, horizontal, output);
                    currentStart = segment.Start;
                    currentEnd = segment.End;
                }

                groupEnd++;
            }

            AddRect(first, currentStart, currentEnd, horizontal, output);
            groupStart = groupEnd;
        }
    }

    private static bool IsSameAxisGroup(in AxisSegment left, in AxisSegment right) =>
        left.Position.CompareTo(right.Position) == 0
        && left.StrokeWidth.CompareTo(right.StrokeWidth) == 0
        && string.Equals(left.Color, right.Color, StringComparison.Ordinal);

    private static void AddRect(
        in AxisSegment segment,
        double start,
        double end,
        bool horizontal,
        List<BoxRect> output
    )
    {
        output.Add(
            horizontal
                ? new BoxRect(
                    start,
                    segment.Position - segment.StrokeWidth / 2d,
                    end - start,
                    segment.StrokeWidth,
                    segment.Color
                )
                : new BoxRect(
                    segment.Position - segment.StrokeWidth / 2d,
                    start,
                    segment.StrokeWidth,
                    end - start,
                    segment.Color
                )
        );
    }

    private readonly record struct AxisSegment(
        double Position,
        double Start,
        double End,
        string Color,
        double StrokeWidth
    );

    private readonly record struct RoundedCorner(
        char Character,
        double X,
        double Y,
        double Width,
        double Height,
        string Color,
        double StrokeWidth
    );

    private readonly record struct BoxRect(
        double X,
        double Y,
        double Width,
        double Height,
        string Color
    );

    private static void RenderBlockElement(
        SvgWriter sb,
        string text,
        double x,
        double y,
        double cellRectWidth,
        double cellRectHeight,
        string fill,
        SvgElementRegistry? elements
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
            if (elements is null)
            {
                sb.Append("<rect class=\"q\"");
                AppendPositionAttributes(sb, rx, ry);
                sb.Append(" width=\"");
                sb.Append(rw);
                sb.Append("\" height=\"");
                sb.Append(rh);
                sb.Append("\" fill=\"");
                sb.Append(fill);
                sb.Append("\"/>\n");
            }
            else
            {
                elements.AppendRect(sb, "q", rx, ry, rw, rh, fill);
            }
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

    private static string ApplyMask(string value, string[]? maskPatterns)
    {
        if (maskPatterns == null || maskPatterns.Length == 0 || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;
        var nonEmptyPatterns = maskPatterns.Where(p => !string.IsNullOrEmpty(p));
        foreach (var pattern in nonEmptyPatterns)
        {
            var mask = new string('*', pattern.Length);
            result = result.Replace(pattern, mask, StringComparison.Ordinal);
        }
        return result;
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
