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
        // Windows Terminal uses a near-black background distinct from the default dark theme
        if (options.Window is WindowStyle.Windows or WindowStyle.WindowsPc)
        {
            theme = theme.WithBackground("#0c0c0c");
        }
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var targetFrame = options.Frame ?? (session.Events.Count - 1);
        if (targetFrame >= 0)
        {
            emulator.Replay(session, targetFrame);
        }

        var commandHeaderRows = string.IsNullOrEmpty(options.CommandHeader) ? 0 : 1;
        var includeScrollback = options.Frame == null;
        var context = SvgDocumentBuilder.CreateContext(
            emulator.Buffer,
            options.Crop,
            includeScrollback,
            options.Window,
            options.Padding,
            options.HeightRows,
            commandHeaderRows,
            options.FontSize
        );
        var sb = new StringBuilder(32 * 1024);
        SvgDocumentBuilder.BeginSvg(
            sb,
            context,
            theme,
            additionalCss: null,
            font: options.Font,
            windowStyle: options.Window,
            commandHeader: options.CommandHeader,
            opacity: options.Opacity,
            background: options.Background
        );
        SvgDocumentBuilder.AppendFrameGroup(
            sb,
            emulator.Buffer,
            context,
            theme,
            id: null,
            @class: null,
            includeScrollback
        );
        SvgDocumentBuilder.EndSvg(sb, options.Opacity);
        return sb.ToString();
    }
}

internal static class SvgDocumentBuilder
{
    private const double DesktopPadding = 20d;
    private const double DesktopShadowOffset = 6d;
    private const string DefaultFontFamily =
        "ui-monospace,\"Cascadia Mono\",\"Segoe UI Mono\",\"SFMono-Regular\",Menlo,monospace";

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
    }

    public static Context CreateContext(
        ScreenBuffer buffer,
        CropOptions crop,
        bool includeScrollback = false,
        WindowStyle windowStyle = WindowStyle.None,
        double padding = 0d,
        int? heightRows = null,
        int commandHeaderRows = 0,
        double fontSize = 14d
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

        switch (windowStyle)
        {
            case WindowStyle.Macos:
                chromeLeft = 1d;
                chromeTop = 28d;
                chromeRight = 1d;
                chromeBottom = 1d;
                break;
            case WindowStyle.Windows:
                chromeLeft = 1d;
                chromeTop = 35d;
                chromeRight = 1d;
                chromeBottom = 1d;
                break;
            case WindowStyle.MacosPc:
                chromeLeft = DesktopPadding + 1d;
                chromeTop = DesktopPadding + 28d;
                chromeRight = DesktopPadding + 1d + DesktopShadowOffset;
                chromeBottom = DesktopPadding + 1d + DesktopShadowOffset;
                break;
            case WindowStyle.WindowsPc:
                chromeLeft = DesktopPadding + 1d;
                chromeTop = DesktopPadding + 35d;
                chromeRight = DesktopPadding + 1d + DesktopShadowOffset;
                chromeBottom = DesktopPadding + 1d + DesktopShadowOffset;
                break;
            default:
                break;
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
            CanvasWidth = Math.Max(1d, canvasWidth),
            CanvasHeight = Math.Max(1d, canvasHeight),
            ContentOffsetX = contentOffsetX,
            ContentOffsetY = contentOffsetY,
            HeaderRows = commandHeaderRows,
            HeaderOffsetX = headerOffsetX,
            HeaderOffsetY = headerOffsetY,
            FontSize = fontSize,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            BaselineOffset = baselineOffset,
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

    public static void BeginSvg(
        StringBuilder sb,
        Context context,
        Theme theme,
        string? additionalCss,
        string? font = null,
        WindowStyle windowStyle = WindowStyle.None,
        string? commandHeader = null,
        double opacity = 1d,
        string[]? background = null
    )
    {
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        sb.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
        sb.Append("viewBox=\"0 0 ");
        sb.Append(Format(context.CanvasWidth));
        sb.Append(' ');
        sb.Append(Format(context.CanvasHeight));
        sb.Append("\" role=\"img\" aria-label=\"console2svg output\">\n");

        var effectiveFont = string.IsNullOrWhiteSpace(font)
            ? DefaultFontFamily
            : EscapeAttribute(font);
        sb.Append("<style>");
        sb.Append(".crt{font-family:");
        sb.Append(effectiveFont);
        sb.Append(';');
        sb.Append("font-size:");
        sb.Append(Format(context.FontSize));
        sb.Append("px;}");
        sb.Append("text{dominant-baseline:alphabetic;}");
        sb.Append(".bg{shape-rendering:crispEdges;}");
        if (!string.IsNullOrWhiteSpace(additionalCss))
        {
            sb.Append(additionalCss);
        }

        sb.Append("</style>\n");

        AppendDefs(sb, context, windowStyle, background);
        AppendBackground(sb, context, windowStyle, background);
        AppendGroupOpen(sb, opacity);
        AppendChrome(sb, context, theme, windowStyle);
        if (context.HeaderRows > 0 && !string.IsNullOrEmpty(commandHeader))
        {
            AppendCommandHeader(sb, context, theme, commandHeader);
        }
    }

    private static void AppendCommandHeader(
        StringBuilder sb,
        Context context,
        Theme theme,
        string commandHeader
    )
    {
        var x = context.HeaderOffsetX;
        var bgY = context.HeaderOffsetY;
        var bgH = context.HeaderRows * context.CellHeight;
        sb.Append("<rect x=\"");
        sb.Append(Format(x));
        sb.Append("\" y=\"");
        sb.Append(Format(bgY));
        sb.Append("\" width=\"");
        sb.Append(Format(context.ViewWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(bgH));
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");
        sb.Append("<text class=\"crt\" x=\"");
        sb.Append(Format(x));
        sb.Append("\" y=\"");
        sb.Append(Format(bgY + context.BaselineOffset));
        sb.Append("\" fill=\"");
        sb.Append(theme.Foreground);
        sb.Append("\">");
        sb.Append(EscapeText(commandHeader));
        sb.Append("</text>\n");
    }

    /// <summary>Renders the always-opaque background layer (desktop bg for *-pc, canvas bg otherwise).</summary>
    private static void AppendBackground(
        StringBuilder sb,
        Context context,
        WindowStyle windowStyle,
        string[]? background = null
    )
    {
        switch (windowStyle)
        {
            case WindowStyle.MacosPc:
            case WindowStyle.WindowsPc:
                // Desktop background only — shadow + chrome go in AppendChrome (inside the single opacity group)
                sb.Append("<rect width=\"");
                sb.Append(Format(context.CanvasWidth));
                sb.Append("\" height=\"");
                sb.Append(Format(context.CanvasHeight));
                sb.Append("\" fill=\"");
                sb.Append(GetDesktopBgFill(background));
                sb.Append("\"/>\n");
                break;
            default:
                AppendCanvasBackground(sb, context, windowStyle, background);
                break;
        }
    }

    /// <summary>Renders chrome elements. No opacity wrapper — caller owns the single outer g opacity group.</summary>
    private static void AppendChrome(
        StringBuilder sb,
        Context context,
        Theme theme,
        WindowStyle windowStyle
    )
    {
        switch (windowStyle)
        {
            case WindowStyle.Macos:
            {
                sb.Append("<rect x=\"0.5\" y=\"0.5\" rx=\"10\" ry=\"10\" width=\"");
                sb.Append(Format(Math.Max(1d, context.CanvasWidth - 1d)));
                sb.Append("\" height=\"");
                sb.Append(Format(Math.Max(1d, context.CanvasHeight - 1d)));
                sb.Append("\" fill=\"#1f1f1f\" stroke=\"#3a3a3a\"/>\n");
                sb.Append("<rect x=\"1\" y=\"1\" rx=\"9\" ry=\"9\" width=\"");
                sb.Append(Format(Math.Max(1d, context.CanvasWidth - 2d)));
                sb.Append("\" height=\"27\" fill=\"#2c2c2c\"/>\n");
                sb.Append("<circle cx=\"14\" cy=\"14\" r=\"5\" fill=\"#ff5f57\"/>\n");
                sb.Append("<circle cx=\"30\" cy=\"14\" r=\"5\" fill=\"#febc2e\"/>\n");
                sb.Append("<circle cx=\"46\" cy=\"14\" r=\"5\" fill=\"#28c840\"/>\n");
                return;
            }
            case WindowStyle.Windows:
            {
                AppendWindowsTerminalChrome(
                    sb,
                    0d,
                    0d,
                    context.CanvasWidth,
                    context.CanvasHeight,
                    theme
                );
                return;
            }
            case WindowStyle.MacosPc:
            {
                var winX = DesktopPadding;
                var winY = DesktopPadding;
                var winW = context.CanvasWidth - 2d * DesktopPadding - DesktopShadowOffset;
                var winH = context.CanvasHeight - 2d * DesktopPadding - DesktopShadowOffset;
                // Shadow — inside the single opacity group so it fades with the window
                sb.Append("<rect x=\"");
                sb.Append(Format(winX + DesktopShadowOffset));
                sb.Append("\" y=\"");
                sb.Append(Format(winY + DesktopShadowOffset));
                sb.Append("\" rx=\"10\" ry=\"10\" width=\"");
                sb.Append(Format(winW));
                sb.Append("\" height=\"");
                sb.Append(Format(winH));
                sb.Append("\" fill=\"black\" fill-opacity=\"0.3\"/>\n");
                sb.Append("<rect x=\"");
                sb.Append(Format(winX + 0.5d));
                sb.Append("\" y=\"");
                sb.Append(Format(winY + 0.5d));
                sb.Append("\" rx=\"10\" ry=\"10\" width=\"");
                sb.Append(Format(winW - 1d));
                sb.Append("\" height=\"");
                sb.Append(Format(winH - 1d));
                sb.Append("\" fill=\"#1f1f1f\" stroke=\"#3a3a3a\"/>\n");
                sb.Append("<rect x=\"");
                sb.Append(Format(winX + 1d));
                sb.Append("\" y=\"");
                sb.Append(Format(winY + 1d));
                sb.Append("\" rx=\"9\" ry=\"9\" width=\"");
                sb.Append(Format(winW - 2d));
                sb.Append("\" height=\"27\" fill=\"#2c2c2c\"/>\n");
                sb.Append("<circle cx=\"");
                sb.Append(Format(winX + 14d));
                sb.Append("\" cy=\"");
                sb.Append(Format(winY + 14d));
                sb.Append("\" r=\"5\" fill=\"#ff5f57\"/>\n");
                sb.Append("<circle cx=\"");
                sb.Append(Format(winX + 30d));
                sb.Append("\" cy=\"");
                sb.Append(Format(winY + 14d));
                sb.Append("\" r=\"5\" fill=\"#febc2e\"/>\n");
                sb.Append("<circle cx=\"");
                sb.Append(Format(winX + 46d));
                sb.Append("\" cy=\"");
                sb.Append(Format(winY + 14d));
                sb.Append("\" r=\"5\" fill=\"#28c840\"/>\n");
                return;
            }
            case WindowStyle.WindowsPc:
            {
                var winX = DesktopPadding;
                var winY = DesktopPadding;
                var winW = context.CanvasWidth - 2d * DesktopPadding - DesktopShadowOffset;
                var winH = context.CanvasHeight - 2d * DesktopPadding - DesktopShadowOffset;
                // Shadow — inside the single opacity group so it fades with the window
                sb.Append("<rect x=\"");
                sb.Append(Format(winX + DesktopShadowOffset));
                sb.Append("\" y=\"");
                sb.Append(Format(winY + DesktopShadowOffset));
                sb.Append("\" width=\"");
                sb.Append(Format(winW));
                sb.Append("\" height=\"");
                sb.Append(Format(winH));
                sb.Append("\" fill=\"black\" fill-opacity=\"0.25\"/>\n");
                AppendWindowsTerminalChrome(sb, winX, winY, winW, winH, theme);
                return;
            }
            default:
                // None: no chrome elements
                return;
        }
    }

    /// <summary>
    /// For non-*-pc window styles, renders the canvas-level background rect.
    /// When opacity &lt; 1 and no explicit background is given, the rect is omitted for
    /// None style (the SVG becomes transparent behind the terminal content).
    /// </summary>
    private static void AppendCanvasBackground(
        StringBuilder sb,
        Context context,
        WindowStyle windowStyle,
        string[]? background
    )
    {
        // Determine the fill
        string? fill = null;
        if (background is { Length: 1 } && !IsImagePath(background[0]))
            fill = background[0]; // solid color
        else if (
            background is { Length: >= 2 }
            || (background is { Length: 1 } && IsImagePath(background[0]))
        )
            fill = "url(#desktop-bg)"; // gradient / image
        else if (windowStyle != WindowStyle.None)
            fill = null; // macos/windows: no background rect needed (outer window rect provides fill)
        // else None without --background: omit background rect → transparent canvas

        if (fill == null)
            return;

        sb.Append("<rect width=\"");
        sb.Append(Format(context.CanvasWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.CanvasHeight));
        sb.Append("\" fill=\"");
        sb.Append(fill);
        sb.Append("\"/>\n"); // always fully opaque
    }

    /// <summary>Opens a &lt;g opacity&gt; group if opacity &lt; 1.</summary>
    private static void AppendGroupOpen(StringBuilder sb, double opacity)
    {
        if (opacity < 1d)
        {
            sb.Append("<g opacity=\"");
            sb.Append(Format(opacity));
            sb.Append("\">\n");
        }
    }

    /// <summary>Closes a &lt;g&gt; group previously opened by AppendGroupOpen.</summary>
    private static void AppendGroupClose(StringBuilder sb, double opacity)
    {
        if (opacity < 1d)
        {
            sb.Append("</g>\n");
        }
    }

    /// <summary>
    /// Returns the desktop background fill value for *-pc window styles.
    /// Uses a default gradient (url(#desktop-bg)) when no user background is specified.
    /// </summary>
    private static string GetDesktopBgFill(string[]? background)
    {
        if (background is { Length: 1 } && !IsImagePath(background[0]))
            return background[0]; // solid user color
        // gradient (2 colors), image, or default → reference defs
        return "url(#desktop-bg)";
    }

    /// <summary>Emits SVG &lt;defs&gt; containing gradient or image background definitions if needed.</summary>
    private static void AppendDefs(
        StringBuilder sb,
        Context context,
        WindowStyle windowStyle,
        string[]? background
    )
    {
        bool isPcStyle = windowStyle is WindowStyle.MacosPc or WindowStyle.WindowsPc;

        // Determine if <defs> are needed
        bool needsDefs;
        if (background is { Length: 1 } && !IsImagePath(background[0]))
            needsDefs = false; // solid color — no defs needed
        else if (background is { Length: >= 2 })
            needsDefs = true; // user gradient
        else if (background is { Length: 1 } && IsImagePath(background[0]))
            needsDefs = true; // user image
        else
            needsDefs = isPcStyle; // default gradient for *-pc styles

        if (!needsDefs)
            return;

        sb.Append("<defs>\n");

        if (background is { Length: 1 } && IsImagePath(background[0]))
        {
            AppendImagePatternDef(sb, background[0], context.CanvasWidth, context.CanvasHeight);
        }
        else if (background is { Length: >= 2 })
        {
            AppendLinearGradientDef(sb, "desktop-bg", background[0], background[1]);
        }
        else
        {
            // Default gradient for *-pc styles — subtle diagonal to avoid being too flashy
            var (c1, c2) =
                windowStyle == WindowStyle.MacosPc
                    ? ("#1a1d2e", "#252840")
                    : ("#1a2535", "#253345");
            AppendLinearGradientDef(sb, "desktop-bg", c1, c2);
        }

        sb.Append("</defs>\n");
    }

    private static void AppendLinearGradientDef(
        StringBuilder sb,
        string id,
        string color1,
        string color2
    )
    {
        sb.Append("<linearGradient id=\"");
        sb.Append(EscapeAttribute(id));
        sb.Append("\" x1=\"0%\" y1=\"0%\" x2=\"100%\" y2=\"100%\">");
        sb.Append("<stop offset=\"0%\" stop-color=\"");
        sb.Append(EscapeAttribute(color1));
        sb.Append("\"/>");
        sb.Append("<stop offset=\"100%\" stop-color=\"");
        sb.Append(EscapeAttribute(color2));
        sb.Append("\"/>");
        sb.Append("</linearGradient>\n");
    }

    private static void AppendImagePatternDef(
        StringBuilder sb,
        string imagePath,
        double width,
        double height
    )
    {
        string href;
        var mimeType = GetImageMimeType(imagePath);
        if (
            imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            href = imagePath;
        }
        else if (System.IO.File.Exists(imagePath))
        {
            var bytes = System.IO.File.ReadAllBytes(imagePath);
            href = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        else
        {
            href = imagePath; // fallback: use as-is
        }

        sb.Append("<pattern id=\"desktop-bg\" patternUnits=\"userSpaceOnUse\" width=\"");
        sb.Append(Format(width));
        sb.Append("\" height=\"");
        sb.Append(Format(height));
        sb.Append("\">");
        sb.Append("<image href=\"");
        sb.Append(EscapeAttribute(href));
        sb.Append("\" width=\"");
        sb.Append(Format(width));
        sb.Append("\" height=\"");
        sb.Append(Format(height));
        sb.Append("\" preserveAspectRatio=\"xMidYMid slice\"/>");
        sb.Append("</pattern>\n");
    }

    private static bool IsImagePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var lower = value.ToLowerInvariant();
        return lower.EndsWith(".png", StringComparison.Ordinal)
            || lower.EndsWith(".jpg", StringComparison.Ordinal)
            || lower.EndsWith(".jpeg", StringComparison.Ordinal)
            || lower.EndsWith(".gif", StringComparison.Ordinal)
            || lower.EndsWith(".svg", StringComparison.Ordinal)
            || lower.EndsWith(".webp", StringComparison.Ordinal)
            || lower.EndsWith(".bmp", StringComparison.Ordinal)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetImageMimeType(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
    }

    private static void AppendWindowsTerminalChrome(
        StringBuilder sb,
        double winX,
        double winY,
        double winW,
        double winH,
        Theme theme
    )
    {
        const double TabBarHeight = 35d;
        const double TabTop = 6d;
        const double TabWidth = 160d;
        const double TabRadius = 5d;
        // Each window-control button column width (wider spacing matching real Windows Terminal)
        const double BtnColW = 46d;

        // Outer window border
        sb.Append("<rect x=\"");
        sb.Append(Format(winX + 0.5d));
        sb.Append("\" y=\"");
        sb.Append(Format(winY + 0.5d));
        sb.Append("\" width=\"");
        sb.Append(Format(winW - 1d));
        sb.Append("\" height=\"");
        sb.Append(Format(winH - 1d));
        sb.Append("\" fill=\"#1c1c1c\" stroke=\"#3a3a3a\"/>\n");

        // Tab bar background
        sb.Append("<rect x=\"");
        sb.Append(Format(winX + 1d));
        sb.Append("\" y=\"");
        sb.Append(Format(winY + 1d));
        sb.Append("\" width=\"");
        sb.Append(Format(winW - 2d));
        sb.Append("\" height=\"");
        sb.Append(Format(TabBarHeight - 1d));
        sb.Append("\" fill=\"#1c1c1c\"/>\n");

        // Active tab: fill = terminal background color (matches content area)
        var tbLeft = winX + 8d;
        var tbTop = winY + TabTop;
        var tbBottom = winY + TabBarHeight;
        var tbRight = tbLeft + TabWidth;
        sb.Append("<path d=\"M ");
        sb.Append(Format(tbLeft));
        sb.Append(',');
        sb.Append(Format(tbBottom));
        sb.Append(" L ");
        sb.Append(Format(tbLeft));
        sb.Append(',');
        sb.Append(Format(tbTop + TabRadius));
        sb.Append(" Q ");
        sb.Append(Format(tbLeft));
        sb.Append(',');
        sb.Append(Format(tbTop));
        sb.Append(' ');
        sb.Append(Format(tbLeft + TabRadius));
        sb.Append(',');
        sb.Append(Format(tbTop));
        sb.Append(" L ");
        sb.Append(Format(tbRight - TabRadius));
        sb.Append(',');
        sb.Append(Format(tbTop));
        sb.Append(" Q ");
        sb.Append(Format(tbRight));
        sb.Append(',');
        sb.Append(Format(tbTop));
        sb.Append(' ');
        sb.Append(Format(tbRight));
        sb.Append(',');
        sb.Append(Format(tbTop + TabRadius));
        sb.Append(" L ");
        sb.Append(Format(tbRight));
        sb.Append(',');
        sb.Append(Format(tbBottom));
        sb.Append(" Z\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");

        // Tab icon ">_" — 2px lower than center
        var iconX = tbLeft + 8d;
        var iconY = winY + TabBarHeight / 2d + 2d;
        // ">" chevron (vector polyline)
        sb.Append("<polyline points=\"");
        sb.Append(Format(iconX));
        sb.Append(',');
        sb.Append(Format(iconY - 4d));
        sb.Append(' ');
        sb.Append(Format(iconX + 4d));
        sb.Append(',');
        sb.Append(Format(iconY));
        sb.Append(' ');
        sb.Append(Format(iconX));
        sb.Append(',');
        sb.Append(Format(iconY + 4d));
        sb.Append(
            "\" fill=\"none\" stroke=\"#cccccc\" stroke-width=\"1.2\" stroke-linejoin=\"round\"/>\n"
        );
        // "_" underline (vector line)
        sb.Append("<line x1=\"");
        sb.Append(Format(iconX + 7d));
        sb.Append("\" y1=\"");
        sb.Append(Format(iconY + 4.5d));
        sb.Append("\" x2=\"");
        sb.Append(Format(iconX + 13d));
        sb.Append("\" y2=\"");
        sb.Append(Format(iconY + 4.5d));
        sb.Append("\" stroke=\"#cccccc\" stroke-width=\"1.2\"/>\n");

        // Tab close button × (inside tab, right side) — aligned to iconY
        var tabCloseCX = tbRight - 12d;
        var tabCloseCY = iconY;
        const double TabCloseR = 3.5d;
        sb.Append("<line x1=\"");
        sb.Append(Format(tabCloseCX - TabCloseR));
        sb.Append("\" y1=\"");
        sb.Append(Format(tabCloseCY - TabCloseR));
        sb.Append("\" x2=\"");
        sb.Append(Format(tabCloseCX + TabCloseR));
        sb.Append("\" y2=\"");
        sb.Append(Format(tabCloseCY + TabCloseR));
        sb.Append("\" stroke=\"#888888\" stroke-width=\"1.1\"/>\n");
        sb.Append("<line x1=\"");
        sb.Append(Format(tabCloseCX + TabCloseR));
        sb.Append("\" y1=\"");
        sb.Append(Format(tabCloseCY - TabCloseR));
        sb.Append("\" x2=\"");
        sb.Append(Format(tabCloseCX - TabCloseR));
        sb.Append("\" y2=\"");
        sb.Append(Format(tabCloseCY + TabCloseR));
        sb.Append("\" stroke=\"#888888\" stroke-width=\"1.1\"/>\n");

        // "+" new-tab button — aligned to iconY
        var plusCX = tbRight + 16d;
        var plusCY = iconY;
        const double PlusR = 5d;
        sb.Append("<line x1=\"");
        sb.Append(Format(plusCX - PlusR));
        sb.Append("\" y1=\"");
        sb.Append(Format(plusCY));
        sb.Append("\" x2=\"");
        sb.Append(Format(plusCX + PlusR));
        sb.Append("\" y2=\"");
        sb.Append(Format(plusCY));
        sb.Append("\" stroke=\"#888888\" stroke-width=\"1.2\"/>\n");
        sb.Append("<line x1=\"");
        sb.Append(Format(plusCX));
        sb.Append("\" y1=\"");
        sb.Append(Format(plusCY - PlusR));
        sb.Append("\" x2=\"");
        sb.Append(Format(plusCX));
        sb.Append("\" y2=\"");
        sb.Append(Format(plusCY + PlusR));
        sb.Append("\" stroke=\"#888888\" stroke-width=\"1.2\"/>\n");

        // "|" vertical separator
        var sepX = tbRight + 36d;
        sb.Append("<line x1=\"");
        sb.Append(Format(sepX));
        sb.Append("\" y1=\"");
        sb.Append(Format(winY + 9d));
        sb.Append("\" x2=\"");
        sb.Append(Format(sepX));
        sb.Append("\" y2=\"");
        sb.Append(Format(winY + TabBarHeight - 9d));
        sb.Append("\" stroke=\"#4a4a4a\" stroke-width=\"1\"/>\n");

        // "v" dropdown chevron — aligned to iconY
        var dropCX = tbRight + 52d;
        var dropCY = iconY;
        sb.Append("<polyline points=\"");
        sb.Append(Format(dropCX - 4d));
        sb.Append(',');
        sb.Append(Format(dropCY - 2d));
        sb.Append(' ');
        sb.Append(Format(dropCX));
        sb.Append(',');
        sb.Append(Format(dropCY + 2d));
        sb.Append(' ');
        sb.Append(Format(dropCX + 4d));
        sb.Append(',');
        sb.Append(Format(dropCY - 2d));
        sb.Append(
            "\" fill=\"none\" stroke=\"#888888\" stroke-width=\"1.2\" stroke-linejoin=\"round\"/>\n"
        );

        // Window control buttons: each occupies BtnColW=46px column, icons centered
        var btnCenterY = iconY;
        var closeX = winX + winW - BtnColW / 2d;
        var maxX = closeX - BtnColW;
        var minX = maxX - BtnColW;
        const double IconHalf = 5d;

        // Minimize ( — )
        sb.Append("<line x1=\"");
        sb.Append(Format(minX - IconHalf));
        sb.Append("\" y1=\"");
        sb.Append(Format(btnCenterY));
        sb.Append("\" x2=\"");
        sb.Append(Format(minX + IconHalf));
        sb.Append("\" y2=\"");
        sb.Append(Format(btnCenterY));
        sb.Append("\" stroke=\"#cccccc\" stroke-width=\"1.2\"/>\n");

        // Maximize ( □ )
        sb.Append("<rect x=\"");
        sb.Append(Format(maxX - IconHalf));
        sb.Append("\" y=\"");
        sb.Append(Format(btnCenterY - IconHalf));
        sb.Append("\" width=\"");
        sb.Append(Format(IconHalf * 2d));
        sb.Append("\" height=\"");
        sb.Append(Format(IconHalf * 2d));
        sb.Append("\" fill=\"none\" stroke=\"#cccccc\" stroke-width=\"1.2\"/>\n");

        // Close ( × )
        sb.Append("<line x1=\"");
        sb.Append(Format(closeX - IconHalf));
        sb.Append("\" y1=\"");
        sb.Append(Format(btnCenterY - IconHalf));
        sb.Append("\" x2=\"");
        sb.Append(Format(closeX + IconHalf));
        sb.Append("\" y2=\"");
        sb.Append(Format(btnCenterY + IconHalf));
        sb.Append("\" stroke=\"#cccccc\" stroke-width=\"1.3\"/>\n");
        sb.Append("<line x1=\"");
        sb.Append(Format(closeX + IconHalf));
        sb.Append("\" y1=\"");
        sb.Append(Format(btnCenterY - IconHalf));
        sb.Append("\" x2=\"");
        sb.Append(Format(closeX - IconHalf));
        sb.Append("\" y2=\"");
        sb.Append(Format(btnCenterY + IconHalf));
        sb.Append("\" stroke=\"#cccccc\" stroke-width=\"1.3\"/>\n");
        // Inner area (content + padding) is rendered by AppendFrameGroup — do not duplicate here
    }

    public static void EndSvg(StringBuilder sb, double opacity = 1d)
    {
        AppendGroupClose(sb, opacity);
        sb.Append("</svg>");
    }

    public static void AppendFrameGroup(
        StringBuilder sb,
        ScreenBuffer buffer,
        Context context,
        Theme theme,
        string? id,
        string? @class,
        bool includeScrollback = false,
        double opacity = 1d
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
                fgUnderline = false;
            int fgRunCellCount = 0;

            void FlushFgRun()
            {
                if (fgRunCellCount == 0 || fgRunColor == null)
                {
                    return;
                }

                var tx = (fgRunStart - context.StartCol) * context.CellWidth;
                var tLen = fgRunCellCount * context.CellWidth;
                sb.Append("<text class=\"crt\" x=\"");
                sb.Append(Format(tx));
                sb.Append("\" y=\"");
                sb.Append(Format(y + context.BaselineOffset));
                sb.Append("\" fill=\"");
                sb.Append(fgRunColor);
                sb.Append("\" textLength=\"");
                sb.Append(Format(tLen));
                sb.Append("\" lengthAdjust=\"spacingAndGlyphs\"");
                if (fgBold || fgItalic || fgUnderline)
                {
                    sb.Append(" style=\"");
                    if (fgBold)
                        sb.Append("font-weight:bold;");
                    if (fgItalic)
                        sb.Append("font-style:italic;");
                    if (fgUnderline)
                        sb.Append("text-decoration:underline;");
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

                var effectiveFg = cell.Reversed ? cell.Background : cell.Foreground;
                effectiveFg = ApplyIntensity(effectiveFg, cell.Bold, cell.Faint);
                effectiveFg = ApplyContextualMatrixTint(
                    buffer,
                    row,
                    col,
                    includeScrollback,
                    effectiveFg,
                    theme
                );

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

                var sameStyle =
                    string.Equals(effectiveFg, fgRunColor, StringComparison.OrdinalIgnoreCase)
                    && cell.Bold == fgBold
                    && cell.Italic == fgItalic
                    && cell.Underline == fgUnderline
                    && !cell.IsWide;

                if (!sameStyle)
                {
                    FlushFgRun();
                    fgRunStart = col;
                    fgRunColor = effectiveFg;
                    fgBold = cell.Bold;
                    fgItalic = cell.Italic;
                    fgUnderline = cell.Underline;
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
                break; // ▁ Lower 1/8
            case 0x2582:
                R(x, y + h * 3d / 4, w, h / 4d);
                break; // ▂ Lower 1/4
            case 0x2583:
                R(x, y + h * 5d / 8, w, h * 3d / 8);
                break; // ▃ Lower 3/8
            case 0x2584:
                R(x, y + hh, w, hh);
                break; // ▄ Lower half
            case 0x2585:
                R(x, y + h * 3d / 8, w, h * 5d / 8);
                break; // ▅ Lower 5/8
            case 0x2586:
                R(x, y + h / 4d, w, h * 3d / 4);
                break; // ▆ Lower 3/4
            case 0x2587:
                R(x, y + h / 8d, w, h * 7d / 8);
                break; // ▇ Lower 7/8
            case 0x2588:
                R(x, y, w, h);
                break; // █ Full block
            case 0x2589:
                R(x, y, w * 7d / 8, h);
                break; // ▉ Left 7/8
            case 0x258A:
                R(x, y, w * 3d / 4, h);
                break; // ▊ Left 3/4
            case 0x258B:
                R(x, y, w * 5d / 8, h);
                break; // ▋ Left 5/8
            case 0x258C:
                R(x, y, hw, h);
                break; // ▌ Left half
            case 0x258D:
                R(x, y, w * 3d / 8, h);
                break; // ▍ Left 3/8
            case 0x258E:
                R(x, y, w / 4d, h);
                break; // ▎ Left 1/4
            case 0x258F:
                R(x, y, w / 8d, h);
                break; // ▏ Left 1/8
            case 0x2590:
                R(x + hw, y, hw, h);
                break; // ▐ Right half
            // 0x2591–0x2593: shade chars handled by font (IsBlockElement returns false)
            case 0x2594:
                R(x, y, w, h / 8d);
                break; // ▔ Upper 1/8
            case 0x2595:
                R(x + w * 7d / 8, y, w / 8d, h);
                break; // ▕ Right 1/8
            case 0x2596:
                R(x, y + hh, hw, hh);
                break; // ▖ Quad lower-left
            case 0x2597:
                R(x + hw, y + hh, hw, hh);
                break; // ▗ Quad lower-right
            case 0x2598:
                R(x, y, hw, hh);
                break; // ▘ Quad upper-left
            case 0x2599:
                R(x, y, hw, hh);
                R(x, y + hh, w, hh);
                break; // ▙
            case 0x259A:
                R(x, y, hw, hh);
                R(x + hw, y + hh, hw, hh);
                break; // ▚
            case 0x259B:
                R(x, y, w, hh);
                R(x, y + hh, hw, hh);
                break; // ▛
            case 0x259C:
                R(x, y, w, hh);
                R(x + hw, y + hh, hw, hh);
                break; // ▜
            case 0x259D:
                R(x + hw, y, hw, hh);
                break; // ▝ Quad upper-right
            case 0x259E:
                R(x + hw, y, hw, hh);
                R(x, y + hh, hw, hh);
                break; // ▞
            case 0x259F:
                R(x + hw, y, hw, hh);
                R(x, y + hh, w, hh);
                break; // ▟
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

    private static string ApplyContextualMatrixTint(
        ScreenBuffer buffer,
        int row,
        int col,
        bool includeScrollback,
        string effectiveForeground,
        Theme theme
    )
    {
        if (
            !string.Equals(
                effectiveForeground,
                theme.AnsiPalette[7],
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return effectiveForeground;
        }

        if (
            HasNeighborGreen(buffer, row - 1, col, includeScrollback, theme)
            || HasNeighborGreen(buffer, row + 1, col, includeScrollback, theme)
            || HasNeighborGreen(buffer, row, col - 1, includeScrollback, theme)
            || HasNeighborGreen(buffer, row, col + 1, includeScrollback, theme)
        )
        {
            return theme.AnsiPalette[10];
        }

        return effectiveForeground;
    }

    private static bool HasNeighborGreen(
        ScreenBuffer buffer,
        int row,
        int col,
        bool includeScrollback,
        Theme theme
    )
    {
        if (col < 0 || col >= buffer.Width)
        {
            return false;
        }

        var maxRows = includeScrollback ? buffer.TotalHeight : buffer.Height;
        if (row < 0 || row >= maxRows)
        {
            return false;
        }

        var cell = includeScrollback ? buffer.GetCellFromTop(row, col) : buffer.GetCell(row, col);
        var fg = cell.Reversed ? cell.Background : cell.Foreground;
        return string.Equals(fg, theme.AnsiPalette[2], StringComparison.OrdinalIgnoreCase)
            || string.Equals(fg, theme.AnsiPalette[10], StringComparison.OrdinalIgnoreCase);
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
