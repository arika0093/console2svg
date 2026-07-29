using System;
using System.Globalization;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal static partial class SvgDocumentBuilder
{
    private const string DefaultFontFamily = $"""
        "JetBrains Mono","Cascadia Mono","Segoe UI Mono","Noto Sans Mono","SFMono-Regular",Menlo,Consolas,"DejaVu Sans Mono","Liberation Mono",monospace
        """;

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

        public double CanvasWidth { get; set; }

        public double CanvasHeight { get; set; }

        public double ContentOffsetX { get; set; }

        public double ContentOffsetY { get; set; }

        public int HeaderRows { get; set; }

        public double HeaderOffsetX { get; set; }

        public double HeaderOffsetY { get; set; }

        // Font metrics derived from the configured font size
        public double FontSize { get; set; }

        public double CellWidth { get; set; }

        public double CellHeight { get; set; }

        public double BaselineOffset { get; set; }

        // Output SVG dimensions (may differ from CanvasWidth/CanvasHeight when --size is used)
        public double OutputWidth { get; set; }

        public double OutputHeight { get; set; }

        // ViewBox origin (negative when canvas is smaller than the target output size)
        public double ViewBoxX { get; set; }

        public double ViewBoxY { get; set; }

        public double ViewBoxWidth { get; set; }

        public double ViewBoxHeight { get; set; }

        /// <summary>True when the viewBox origin is offset (i.e. output is larger than the natural canvas).</summary>
        public bool HasViewBoxOffset { get; set; }
    }

    public static Context CreateContext(
        ScreenBuffer buffer,
        CropOptions crop,
        bool includeScrollback = false,
        ChromeDefinition? chrome = null,
        double padding = 0d,
        int? heightRows = null,
        int commandHeaderRows = 0,
        double fontSize = 14d,
        double? sizeWidth = null,
        double? sizeHeight = null
    )
    {
        // Derive font metrics from fontSize
        var cellWidth = fontSize * 0.6d;
        var cellHeight = fontSize * (18d / 14d);
        var baselineOffset = fontSize;

        var effectiveHeight = includeScrollback ? buffer.TotalHeight : buffer.Height;

        var rowTop = crop.Top.Unit switch
        {
            CropUnit.Characters => (int)Math.Floor(crop.Top.Value),
            CropUnit.Text => ApplyTextOffset(
                FindFirstRowContaining(
                    buffer,
                    crop.Top.TextPattern,
                    effectiveHeight,
                    includeScrollback
                ),
                crop.Top.TextOffset
            ),
            _ => 0,
        };
        var rowBottom = crop.Bottom.Unit switch
        {
            CropUnit.Characters => (int)Math.Floor(crop.Bottom.Value),
            CropUnit.Text => effectiveHeight
                - 1
                - ApplyTextOffset(
                    FindLastRowContaining(
                        buffer,
                        crop.Bottom.TextPattern,
                        effectiveHeight,
                        includeScrollback
                    ),
                    crop.Bottom.TextOffset
                ),
            _ => 0,
        };
        var colLeft = crop.Left.Unit == CropUnit.Characters ? (int)Math.Floor(crop.Left.Value) : 0;
        var colRight =
            crop.Right.Unit == CropUnit.Characters ? (int)Math.Floor(crop.Right.Value) : 0;

        rowTop = Clamp(rowTop, 0, effectiveHeight - 1);
        rowBottom = Clamp(rowBottom, 0, effectiveHeight - rowTop - 1);
        colLeft = Clamp(colLeft, 0, buffer.Width - 1);
        colRight = Clamp(colRight, 0, buffer.Width - colLeft - 1);

        var startRow = rowTop;
        var endRowExclusive = effectiveHeight - rowBottom;
        var startCol = colLeft;
        var endColExclusive = buffer.Width - colRight;

        startRow = Clamp(startRow, 0, effectiveHeight - 1);

        if (heightRows.HasValue)
        {
            var maxEndRow = startRow + heightRows.Value;
            endRowExclusive = Math.Min(endRowExclusive, maxEndRow);
            endRowExclusive = Math.Max(endRowExclusive, startRow + 1);
        }

        var contentWidth = Math.Max(1d, (endColExclusive - startCol) * cellWidth);
        var contentHeight = Math.Max(1d, (endRowExclusive - startRow) * cellHeight);

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

        // When -h is specified, preserve the requested height unless px crop is actively reducing height
        if (
            heightRows.HasValue
            && !(crop.Top.Unit == CropUnit.Pixels && crop.Top.Value > 0)
            && !(crop.Bottom.Unit == CropUnit.Pixels && crop.Bottom.Value > 0)
        )
        {
            viewHeight = Math.Max(viewHeight, heightRows.Value * cellHeight);
        }

        var normalizedPadding = Math.Max(0d, padding);
        var chromeLeft = 0d;
        var chromeTop = 0d;
        var chromeRight = 0d;
        var chromeBottom = 0d;

        if (chrome != null)
        {
            if (chrome.IsDesktop)
            {
                chromeLeft = chrome.DesktopPadding + chrome.PaddingLeft;
                chromeTop = chrome.DesktopPadding + chrome.PaddingTop;
                chromeRight = chrome.DesktopPadding + chrome.PaddingRight + chrome.ShadowOffset;
                chromeBottom = chrome.DesktopPadding + chrome.PaddingBottom + chrome.ShadowOffset;
            }
            else
            {
                chromeLeft = chrome.PaddingLeft;
                chromeTop = chrome.PaddingTop;
                chromeRight = chrome.PaddingRight;
                chromeBottom = chrome.PaddingBottom;
            }
        }

        var headerHeight = commandHeaderRows * cellHeight;
        var headerOffsetX = chromeLeft + normalizedPadding;
        var headerOffsetY = chromeTop + normalizedPadding;
        var contentOffsetX = chromeLeft + normalizedPadding;
        var contentOffsetY = chromeTop + normalizedPadding + headerHeight;
        var canvasWidth =
            chromeLeft + chromeRight + normalizedPadding + viewWidth + normalizedPadding;
        var canvasHeight =
            chromeTop
            + chromeBottom
            + normalizedPadding
            + headerHeight
            + viewHeight
            + normalizedPadding;

        var naturalCanvasWidth = Math.Max(1d, canvasWidth);
        var naturalCanvasHeight = Math.Max(1d, canvasHeight);

        // Compute output SVG dimensions and viewBox based on --size constraints.
        double outputWidth;
        double outputHeight;
        double viewBoxX;
        double viewBoxY;
        double viewBoxWidth;
        double viewBoxHeight;

        if (sizeWidth.HasValue && sizeHeight.HasValue)
        {
            // Both specified: scale to fit within the target, then center with extended background.
            var scale = Math.Min(
                sizeWidth.Value / naturalCanvasWidth,
                sizeHeight.Value / naturalCanvasHeight
            );
            var scaledW = naturalCanvasWidth * scale;
            var scaledH = naturalCanvasHeight * scale;
            var marginX = (sizeWidth.Value - scaledW) / 2d;
            var marginY = (sizeHeight.Value - scaledH) / 2d;
            // Express the margins in viewBox coordinate space (where 1 unit = naturalCanvas / output * scale)
            var vOffX = Math.Max(0d, marginX / scale);
            var vOffY = Math.Max(0d, marginY / scale);
            outputWidth = sizeWidth.Value;
            outputHeight = sizeHeight.Value;
            viewBoxX = vOffX > 0d ? -vOffX : 0d;
            viewBoxY = vOffY > 0d ? -vOffY : 0d;
            viewBoxWidth = sizeWidth.Value / scale;
            viewBoxHeight = sizeHeight.Value / scale;
        }
        else if (sizeWidth.HasValue)
        {
            // Width only: scale proportionally.
            var scale = sizeWidth.Value / naturalCanvasWidth;
            outputWidth = sizeWidth.Value;
            outputHeight = naturalCanvasHeight * scale;
            viewBoxX = 0d;
            viewBoxY = 0d;
            viewBoxWidth = naturalCanvasWidth;
            viewBoxHeight = naturalCanvasHeight;
        }
        else if (sizeHeight.HasValue)
        {
            // Height only: scale proportionally.
            var scale = sizeHeight.Value / naturalCanvasHeight;
            outputWidth = naturalCanvasWidth * scale;
            outputHeight = sizeHeight.Value;
            viewBoxX = 0d;
            viewBoxY = 0d;
            viewBoxWidth = naturalCanvasWidth;
            viewBoxHeight = naturalCanvasHeight;
        }
        else
        {
            // No size constraint: output equals natural canvas.
            outputWidth = naturalCanvasWidth;
            outputHeight = naturalCanvasHeight;
            viewBoxX = 0d;
            viewBoxY = 0d;
            viewBoxWidth = naturalCanvasWidth;
            viewBoxHeight = naturalCanvasHeight;
        }

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
            CanvasWidth = naturalCanvasWidth,
            CanvasHeight = naturalCanvasHeight,
            ContentOffsetX = contentOffsetX,
            ContentOffsetY = contentOffsetY,
            HeaderRows = commandHeaderRows,
            HeaderOffsetX = headerOffsetX,
            HeaderOffsetY = headerOffsetY,
            FontSize = fontSize,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            BaselineOffset = baselineOffset,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            ViewBoxX = viewBoxX,
            ViewBoxY = viewBoxY,
            ViewBoxWidth = viewBoxWidth,
            ViewBoxHeight = viewBoxHeight,
            HasViewBoxOffset = viewBoxX < 0d || viewBoxY < 0d,
        };
    }

    private static bool RowContainsPattern(
        ScreenBuffer buffer,
        int row,
        string pattern,
        bool includeScrollback
    )
    {
        var cells = new string[buffer.Width];
        for (var col = 0; col < buffer.Width; col++)
        {
            var cell = includeScrollback
                ? buffer.GetCellFromTop(row, col)
                : buffer.GetCell(row, col);
            cells[col] = cell.Text;
        }

        return string.Concat(cells).Contains(pattern, StringComparison.Ordinal);
    }

    private static int FindFirstRowContaining(
        ScreenBuffer buffer,
        string? pattern,
        int effectiveHeight,
        bool includeScrollback
    )
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

    private static int FindLastRowContaining(
        ScreenBuffer buffer,
        string? pattern,
        int effectiveHeight,
        bool includeScrollback
    )
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

    private static int ApplyTextOffset(int row, int offset)
    {
        return row + offset;
    }
}
