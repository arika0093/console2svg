using System;
using System.Globalization;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

public static class SvgRenderer
{
    public static string Render(RecordingSession session, SvgRenderOptions options)
    {
        var theme = Theme.Resolve(options.Theme);
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var targetFrame = options.Frame ?? (session.Events.Count - 1);
        if (targetFrame >= 0)
        {
            emulator.Replay(session, targetFrame);
        }

        var includeScrollback = options.Frame == null;
        var context = SvgDocumentBuilder.CreateContext(emulator.Buffer, options.Crop, includeScrollback);
        var sb = new StringBuilder(32 * 1024);
        SvgDocumentBuilder.BeginSvg(sb, context, theme, additionalCss: null, font: options.Font);
        SvgDocumentBuilder.AppendFrameGroup(sb, emulator.Buffer, context, theme, id: null, @class: null, includeScrollback);
        SvgDocumentBuilder.EndSvg(sb);
        return sb.ToString();
    }
}

internal static class SvgDocumentBuilder
{
    private const double CellWidth = 9d;
    private const double CellHeight = 18d;
    private const double FontSize = 14d;
    private const double BaselineOffset = 14d;
    private const string DefaultFontFamily = "ui-monospace,\"Cascadia Mono\",\"Segoe UI Mono\",\"SFMono-Regular\",Menlo,monospace";

    internal sealed class Context
    {
        public int StartRow { get; set; }

        public int EndRowExclusive { get; set; }

        public int StartCol { get; set; }

        public int EndColExclusive { get; set; }

        public double ContentWidth { get; set; }

        public double ContentHeight { get; set; }

        public double PixelCropTop { get; set; }

        public double PixelCropRight { get; set; }

        public double PixelCropBottom { get; set; }

        public double PixelCropLeft { get; set; }

        public double ViewWidth { get; set; }

        public double ViewHeight { get; set; }
    }

    public static Context CreateContext(ScreenBuffer buffer, CropOptions crop, bool includeScrollback = false)
    {
        var effectiveHeight = includeScrollback ? buffer.TotalHeight : buffer.Height;

        var rowTop = crop.Top.Unit switch
        {
            CropUnit.Characters => (int)Math.Floor(crop.Top.Value),
            CropUnit.Text => FindFirstRowContaining(buffer, crop.Top.TextPattern, effectiveHeight, includeScrollback),
            _ => 0,
        };
        var rowBottom = crop.Bottom.Unit switch
        {
            CropUnit.Characters => (int)Math.Floor(crop.Bottom.Value),
            CropUnit.Text => effectiveHeight - 1 - FindLastRowContaining(buffer, crop.Bottom.TextPattern, effectiveHeight, includeScrollback),
            _ => 0,
        };
        var colLeft = crop.Left.Unit == CropUnit.Characters ? (int)Math.Floor(crop.Left.Value) : 0;
        var colRight = crop.Right.Unit == CropUnit.Characters ? (int)Math.Floor(crop.Right.Value) : 0;

        rowTop = Clamp(rowTop, 0, effectiveHeight - 1);
        rowBottom = Clamp(rowBottom, 0, effectiveHeight - rowTop - 1);
        colLeft = Clamp(colLeft, 0, buffer.Width - 1);
        colRight = Clamp(colRight, 0, buffer.Width - colLeft - 1);

        var startRow = rowTop;
        var endRowExclusive = effectiveHeight - rowBottom;
        var startCol = colLeft;
        var endColExclusive = buffer.Width - colRight;

        var contentWidth = Math.Max(1d, (endColExclusive - startCol) * CellWidth);
        var contentHeight = Math.Max(1d, (endRowExclusive - startRow) * CellHeight);

        var pxTop = crop.Top.Unit == CropUnit.Pixels ? Math.Max(0d, crop.Top.Value) : 0d;
        var pxRight = crop.Right.Unit == CropUnit.Pixels ? Math.Max(0d, crop.Right.Value) : 0d;
        var pxBottom = crop.Bottom.Unit == CropUnit.Pixels ? Math.Max(0d, crop.Bottom.Value) : 0d;
        var pxLeft = crop.Left.Unit == CropUnit.Pixels ? Math.Max(0d, crop.Left.Value) : 0d;

        pxLeft = Math.Min(pxLeft, Math.Max(0d, contentWidth - 1d));
        pxRight = Math.Min(pxRight, Math.Max(0d, contentWidth - pxLeft - 1d));
        pxTop = Math.Min(pxTop, Math.Max(0d, contentHeight - 1d));
        pxBottom = Math.Min(pxBottom, Math.Max(0d, contentHeight - pxTop - 1d));

        var viewWidth = Math.Max(1d, contentWidth - pxLeft - pxRight);
        var viewHeight = Math.Max(1d, contentHeight - pxTop - pxBottom);

        return new Context
        {
            StartRow = startRow,
            EndRowExclusive = endRowExclusive,
            StartCol = startCol,
            EndColExclusive = endColExclusive,
            ContentWidth = contentWidth,
            ContentHeight = contentHeight,
            PixelCropTop = pxTop,
            PixelCropRight = pxRight,
            PixelCropBottom = pxBottom,
            PixelCropLeft = pxLeft,
            ViewWidth = viewWidth,
            ViewHeight = viewHeight,
        };
    }

    private static bool RowContainsPattern(ScreenBuffer buffer, int row, string pattern, bool includeScrollback)
    {
        var cells = new string[buffer.Width];
        for (var col = 0; col < buffer.Width; col++)
        {
            var cell = includeScrollback ? buffer.GetCellFromTop(row, col) : buffer.GetCell(row, col);
            cells[col] = cell.Text;
        }

        return string.Concat(cells).Contains(pattern, StringComparison.Ordinal);
    }

    private static int FindFirstRowContaining(ScreenBuffer buffer, string? pattern, int effectiveHeight, bool includeScrollback)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return 0;
        }

        for (var row = 0; row < effectiveHeight; row++)
        {
            if (RowContainsPattern(buffer, row, pattern, includeScrollback))
            {
                return row;
            }
        }

        return 0;
    }

    private static int FindLastRowContaining(ScreenBuffer buffer, string? pattern, int effectiveHeight, bool includeScrollback)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return effectiveHeight - 1;
        }

        for (var row = effectiveHeight - 1; row >= 0; row--)
        {
            if (RowContainsPattern(buffer, row, pattern, includeScrollback))
            {
                return row;
            }
        }

        return effectiveHeight - 1;
    }

    public static void BeginSvg(StringBuilder sb, Context context, Theme theme, string? additionalCss, string? font = null)
    {
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        sb.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
        sb.Append("viewBox=\"0 0 ");
        sb.Append(Format(context.ViewWidth));
        sb.Append(' ');
        sb.Append(Format(context.ViewHeight));
        sb.Append("\" role=\"img\" aria-label=\"console2svg output\">\n");

        var effectiveFont = string.IsNullOrWhiteSpace(font) ? DefaultFontFamily : EscapeAttribute(font);
        sb.Append("<style>");
        sb.Append(".crt{font-family:");
        sb.Append(effectiveFont);
        sb.Append(';');
        sb.Append("font-size:");
        sb.Append(Format(FontSize));
        sb.Append("px;}");
        sb.Append("text{dominant-baseline:alphabetic;}");
        if (!string.IsNullOrWhiteSpace(additionalCss))
        {
            sb.Append(additionalCss);
        }

        sb.Append("</style>\n");
        sb.Append("<rect width=\"");
        sb.Append(Format(context.ViewWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ViewHeight));
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");
    }

    public static void EndSvg(StringBuilder sb)
    {
        sb.Append("</svg>");
    }

    public static void AppendFrameGroup(
        StringBuilder sb,
        ScreenBuffer buffer,
        Context context,
        Theme theme,
        string? id,
        string? @class,
        bool includeScrollback = false
    )
    {
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
        sb.Append(Format(-context.PixelCropLeft));
        sb.Append(' ');
        sb.Append(Format(-context.PixelCropTop));
        sb.Append(")\">\n");

        sb.Append("<rect width=\"");
        sb.Append(Format(context.ContentWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ContentHeight));
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");

        for (var row = context.StartRow; row < context.EndRowExclusive; row++)
        {
            for (var col = context.StartCol; col < context.EndColExclusive; col++)
            {
                var cell = includeScrollback
                    ? buffer.GetCellFromTop(row, col)
                    : buffer.GetCell(row, col);
                var x = (col - context.StartCol) * CellWidth;
                var y = (row - context.StartRow) * CellHeight;

                if (cell.IsWideContinuation)
                {
                    continue;
                }

                var cellRectWidth = cell.IsWide ? CellWidth * 2 : CellWidth;
                var effectiveFg = cell.Reversed ? cell.Background : cell.Foreground;
                var effectiveBg = cell.Reversed ? cell.Foreground : cell.Background;

                if (!string.Equals(effectiveBg, theme.Background, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("<rect x=\"");
                    sb.Append(Format(x));
                    sb.Append("\" y=\"");
                    sb.Append(Format(y));
                    sb.Append("\" width=\"");
                    sb.Append(Format(cellRectWidth));
                    sb.Append("\" height=\"");
                    sb.Append(Format(CellHeight));
                    sb.Append("\" fill=\"");
                    sb.Append(effectiveBg);
                    sb.Append("\"/>\n");
                }

                if (cell.Text == " ")
                {
                    continue;
                }

                sb.Append("<text class=\"crt\" x=\"");
                sb.Append(Format(x));
                sb.Append("\" y=\"");
                sb.Append(Format(y + BaselineOffset));
                sb.Append("\" fill=\"");
                sb.Append(effectiveFg);
                sb.Append("\"");

                if (cell.Bold || cell.Italic || cell.Underline)
                {
                    sb.Append(" style=\"");
                    if (cell.Bold)
                    {
                        sb.Append("font-weight:bold;");
                    }

                    if (cell.Italic)
                    {
                        sb.Append("font-style:italic;");
                    }

                    if (cell.Underline)
                    {
                        sb.Append("text-decoration:underline;");
                    }

                    sb.Append("\"");
                }

                sb.Append('>');
                sb.Append(EscapeText(cell.Text));
                sb.Append("</text>\n");
            }
        }

        sb.Append("</g>\n");
    }

    public static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
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
