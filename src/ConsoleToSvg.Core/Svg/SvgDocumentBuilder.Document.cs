using System;
using System.Collections.Generic;
using System.Globalization;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal static partial class SvgDocumentBuilder
{
    public static void BeginSvg(
        SvgWriter sb,
        in Context context,
        Theme theme,
        SvgStyleRegistry styles,
        string? additionalCss,
        string? font = null,
        ChromeDefinition? chrome = null,
        string? commandHeader = null,
        double opacity = 1d,
        string[]? background = null,
        string[]? maskPatterns = null,
        bool animateBlink = false
    )
    {
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        sb.Append("width=\"");
        sb.Append(context.OutputWidth);
        sb.Append("\" height=\"");
        sb.Append(context.OutputHeight);
        sb.Append("\" ");
        sb.Append("viewBox=\"");
        sb.Append(context.ViewBoxX);
        sb.Append(' ');
        sb.Append(context.ViewBoxY);
        sb.Append(' ');
        sb.Append(context.ViewBoxWidth);
        sb.Append(' ');
        sb.Append(context.ViewBoxHeight);
        sb.Append("\" role=\"img\" aria-label=\"console2svg output\">\n");

        var effectiveFont = string.IsNullOrWhiteSpace(font)
            ? DefaultFontFamily
            : EscapeAttribute(font);
        sb.Append(
            $$"""
            <style>
            .c2.c { font-family: {{effectiveFont}}; font-size: {{Format(context.FontSize)}}px; }
            .c2 text { dominant-baseline: alphabetic; }
            .c2 .q { shape-rendering: crispEdges; }
            """
        );
        if (animateBlink)
        {
            sb.Append("\n.c2 .c2b { animation: c2b 1s step-start infinite; }\n");
        }
        if (!string.IsNullOrWhiteSpace(additionalCss))
        {
            sb.Append('\n');
            sb.Append(additionalCss);
            if (!additionalCss.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }

        styles.AppendCss(sb);
        if (animateBlink)
        {
            sb.Append("@keyframes c2b { 50% { visibility: hidden; } }\n");
        }
        sb.Append("</style>");

        AppendDefs(sb, context, chrome, background);
        AppendBackground(sb, context, chrome, background);
        AppendGroupOpen(sb, opacity);
        AppendChrome(sb, context, theme, chrome);
        AppendClientBackground(sb, context, theme, chrome);
        if (context.HeaderRows > 0 && !string.IsNullOrEmpty(commandHeader))
        {
            AppendCommandHeader(sb, context, theme, styles, commandHeader, maskPatterns);
        }
    }

    private static void AppendCommandHeader(
        SvgWriter sb,
        in Context context,
        Theme theme,
        SvgStyleRegistry styles,
        string commandHeader,
        string[]? maskPatterns = null
    )
    {
        var x = context.HeaderOffsetX;
        var bgY = context.HeaderOffsetY;
        var bgH = context.HeaderRows * context.CellHeight;
        sb.Append("<g class=\"c2 c\"><rect");
        AppendPositionAttributes(sb, x, bgY);
        sb.Append(" width=\"");
        sb.Append(context.ViewWidth);
        sb.Append("\" height=\"");
        sb.Append(bgH);
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");
        sb.Append("<text class=\"");
        sb.Append(styles.GetTextClass(theme.Foreground));
        sb.Append('"');
        AppendPositionAttributes(sb, x, bgY + context.BaselineOffset);
        sb.Append(">");
        sb.Append(ApplyMask(EscapeText(commandHeader), maskPatterns));
        sb.Append("</text></g>\n");
    }

    /// <summary>Renders the always-opaque background layer (desktop bg for desktop styles, canvas bg otherwise).</summary>
    private static void AppendBackground(
        SvgWriter sb,
        in Context context,
        ChromeDefinition? chrome,
        string[]? background = null
    )
    {
        if (chrome?.IsDesktop == true)
        {
            // Desktop background only  Eshadow + chrome go in AppendChrome (inside the single opacity group)
            sb.Append("<rect");
            if (context.HasViewBoxOffset)
            {
                AppendPositionAttributes(sb, context.ViewBoxX, context.ViewBoxY);
            }

            sb.Append(" width=\"");
            sb.Append(context.ViewBoxWidth);
            sb.Append("\" height=\"");
            sb.Append(context.ViewBoxHeight);
            sb.Append("\" fill=\"");
            sb.Append(GetDesktopBgFill(background));
            sb.Append("\"/>\n");
        }
        else
        {
            AppendCanvasBackground(sb, context, chrome, background);
        }
    }

    /// <summary>Renders chrome elements via the ChromeDefinition template. No opacity wrapper  Ecaller owns the outer g.</summary>
    private static void AppendChrome(
        SvgWriter sb,
        in Context context,
        Theme theme,
        ChromeDefinition? chrome
    )
    {
        if (chrome == null)
        {
            return;
        }

        double winX,
            winY,
            winW,
            winH;
        if (chrome.IsDesktop)
        {
            winX = chrome.DesktopPadding;
            winY = chrome.DesktopPadding;
            winW = context.CanvasWidth - 2d * chrome.DesktopPadding - chrome.ShadowOffset;
            winH = context.CanvasHeight - 2d * chrome.DesktopPadding - chrome.ShadowOffset;
        }
        else
        {
            winX = 0d;
            winY = 0d;
            winW = context.CanvasWidth;
            winH = context.CanvasHeight;
        }

        sb.Append(
            chrome.Render(
                winX,
                winY,
                winW,
                winH,
                context.CanvasWidth,
                context.CanvasHeight,
                theme.Background
            )
        );
        sb.Append('\n');
    }

    /// <summary>
    /// Fills the terminal client area (inside chrome padding) with the theme background.
    /// Ensures padding space is not transparent when a window chrome is used.
    /// </summary>
    private static void AppendClientBackground(
        SvgWriter sb,
        in Context context,
        Theme theme,
        ChromeDefinition? chrome
    )
    {
        double left,
            top,
            right,
            bottom;
        if (chrome == null)
        {
            left = 0d;
            top = 0d;
            right = 0d;
            bottom = 0d;
        }
        else if (chrome.IsDesktop)
        {
            left = chrome.DesktopPadding + chrome.PaddingLeft;
            top = chrome.DesktopPadding + chrome.PaddingTop;
            right = chrome.DesktopPadding + chrome.PaddingRight + chrome.ShadowOffset;
            bottom = chrome.DesktopPadding + chrome.PaddingBottom + chrome.ShadowOffset;
        }
        else
        {
            left = chrome.PaddingLeft;
            top = chrome.PaddingTop;
            right = chrome.PaddingRight;
            bottom = chrome.PaddingBottom;
        }

        var width = Math.Max(0d, context.CanvasWidth - left - right);
        var height = Math.Max(0d, context.CanvasHeight - top - bottom);
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        sb.Append("<rect");
        AppendPositionAttributes(sb, left, top);
        sb.Append(" width=\"");
        sb.Append(width);
        sb.Append("\" height=\"");
        sb.Append(height);
        if (chrome is { ClientCornerRadius: > 0 })
        {
            sb.Append("\" rx=\"");
            sb.Append(chrome.ClientCornerRadius);
            sb.Append("\" ry=\"");
            sb.Append(chrome.ClientCornerRadius);
        }
        sb.Append("\" fill=\"");
        sb.Append(theme.Background);
        sb.Append("\"/>\n");
    }

    /// <summary>
    /// For non-desktop chrome styles (or no chrome), renders the canvas-level background rect.
    /// When no explicit background is given, the rect is omitted for non-None styles
    /// (the chrome window rect provides fill) and uses the terminal background for None style.
    /// </summary>
    private static void AppendCanvasBackground(
        SvgWriter sb,
        in Context context,
        ChromeDefinition? chrome,
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
            fill = "url(#c2bg)"; // gradient / image
        else if (chrome != null)
            fill = null; // chrome window rect provides the background fill
        // else no chrome and no --background: omit rect ・transparent canvas

        if (fill == null)
            return;

        sb.Append("<rect");
        if (context.HasViewBoxOffset)
        {
            AppendPositionAttributes(sb, context.ViewBoxX, context.ViewBoxY);
        }

        sb.Append(" width=\"");
        sb.Append(context.ViewBoxWidth);
        sb.Append("\" height=\"");
        sb.Append(context.ViewBoxHeight);
        sb.Append("\" fill=\"");
        sb.Append(fill);
        sb.Append("\"/>\n"); // always fully opaque
    }

    /// <summary>Opens a &lt;g opacity&gt; group if opacity &lt; 1.</summary>
    private static void AppendGroupOpen(SvgWriter sb, double opacity)
    {
        if (opacity < 1d)
        {
            sb.Append("<g opacity=\"");
            sb.Append(opacity);
            sb.Append("\">\n");
        }
    }

    /// <summary>Closes a &lt;g&gt; group previously opened by AppendGroupOpen.</summary>
    private static void AppendGroupClose(SvgWriter sb, double opacity)
    {
        if (opacity < 1d)
        {
            sb.Append("</g>\n");
        }
    }

    /// <summary>
    /// Returns the desktop background fill value for *-pc window styles.
    /// Uses a default gradient (url(#c2bg)) when no user background is specified.
    /// </summary>
    private static string GetDesktopBgFill(string[]? background)
    {
        if (background is { Length: 1 } && !IsImagePath(background[0]))
            return background[0]; // solid user color
        // gradient (2 colors), image, or default ↁEreference defs
        return "url(#c2bg)";
    }

    /// <summary>Emits SVG &lt;defs&gt; containing gradient or image background definitions if needed.</summary>
    private static void AppendDefs(
        SvgWriter sb,
        in Context context,
        ChromeDefinition? chrome,
        string[]? background
    )
    {
        bool isDesktopStyle = chrome?.IsDesktop == true;

        // Determine if <defs> are needed
        bool needsDefs;
        if (background is { Length: 1 } && !IsImagePath(background[0]))
            needsDefs = false; // solid color  Eno defs needed
        else if (background is { Length: >= 2 })
            needsDefs = true; // user gradient
        else if (background is { Length: 1 } && IsImagePath(background[0]))
            needsDefs = true; // user image
        else
            needsDefs = isDesktopStyle; // default gradient for desktop styles

        if (!needsDefs)
            return;

        sb.Append("<defs>\n");

        if (background is { Length: 1 } && IsImagePath(background[0]))
        {
            AppendImagePatternDef(sb, background[0], context);
        }
        else if (background is { Length: >= 2 })
        {
            AppendLinearGradientDef(sb, "c2bg", background[0], background[1]);
        }
        else
        {
            // Default gradient from chrome definition  Esubtle diagonal
            var c1 = chrome?.DesktopGradientFrom ?? "#1a1d2e";
            var c2 = chrome?.DesktopGradientTo ?? "#252840";
            AppendLinearGradientDef(sb, "c2bg", c1, c2);
        }

        sb.Append("</defs>\n");
    }

    private static void AppendLinearGradientDef(
        SvgWriter sb,
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
        SvgWriter sb,
        string imagePath,
        in Context context
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

        sb.Append(
            "<pattern id=\"c2bg\" patternUnits=\"userSpaceOnUse\" patternContentUnits=\"userSpaceOnUse\" x=\""
        );
        sb.Append(context.ViewBoxX);
        sb.Append("\" y=\"");
        sb.Append(context.ViewBoxY);
        sb.Append("\" width=\"");
        sb.Append(context.ViewBoxWidth);
        sb.Append("\" height=\"");
        sb.Append(context.ViewBoxHeight);
        sb.Append("\">");
        sb.Append("<image href=\"");
        sb.Append(EscapeAttribute(href));
        sb.Append("\" x=\"");
        sb.Append("0");
        sb.Append("\" y=\"");
        sb.Append("0");
        sb.Append("\" width=\"");
        sb.Append(context.ViewBoxWidth);
        sb.Append("\" height=\"");
        sb.Append(context.ViewBoxHeight);
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

    public static void EndSvg(
        SvgWriter sb,
        double opacity = 1d,
        string? embeddedAsciicast = null,
        string? embeddedLogs = null,
        string? embeddedReplay = null
    )
    {
        AppendGroupClose(sb, opacity);
        if (!string.IsNullOrEmpty(embeddedAsciicast))
        {
            AppendEmbeddedMetadata(sb, "console2svg-asciicast", "asciicast-v2", embeddedAsciicast);
        }
        if (!string.IsNullOrEmpty(embeddedLogs))
        {
            AppendEmbeddedMetadata(sb, "console2svg-logs", "text/plain", embeddedLogs);
        }
        if (!string.IsNullOrEmpty(embeddedReplay))
        {
            AppendEmbeddedMetadata(sb, "console2svg-replay", "console2svg-replay-v1", embeddedReplay);
        }
        sb.Append("</svg>");
    }

    private static void AppendEmbeddedMetadata(
        SvgWriter sb,
        string id,
        string format,
        string base64Data
    )
    {
        sb.Append("<metadata id=\"");
        sb.Append(id);
        sb.Append("\" data-format=\"");
        sb.Append(format);
        sb.Append("\" data-encoding=\"base64\">");
        sb.Append(base64Data);
        sb.Append("</metadata>\n");
    }

    /// <summary>
    /// Renders unique row contents into a &lt;defs&gt; block and returns the row definition
    /// used by each frame.
    /// </summary>
    public static int[][] AppendAnimatedRowDefs(
        SvgWriter sb,
        ReadOnlySpan<TerminalFrame> frames,
        in Context context,
        Theme theme,
        SvgStyleRegistry styles,
        string lengthAdjust,
        double opacity = 1d,
        string[]? maskPatterns = null
    )
    {
        var rowCount = context.EndRowExclusive - context.StartRow;
        var rowDefinitions = new List<RowDefinition>();
        var hashToRowDefinitionIndices =
            new Dictionary<ulong, List<int>>();
        var frameRowDefinitions = new int[frames.Length][];
        var lastDefinitionByRow = new int[rowCount];
        var elements = new SvgElementRegistry();
        Array.Fill(lastDefinitionByRow, -1);

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var buffer = frames[frameIndex].Buffer;
            var rowMappings = new int[rowCount];
            frameRowDefinitions[frameIndex] = rowMappings;

            for (var row = context.StartRow; row < context.EndRowExclusive; row++)
            {
                var signature = buffer.GetRowVisualSignature(row);
                var definitionIndex = -1;
                if (hashToRowDefinitionIndices.TryGetValue(signature, out var candidates))
                {
                    foreach (var candidateIndex in candidates)
                    {
                        var candidate = rowDefinitions[candidateIndex];
                        if (
                            buffer.HasSameVisualRow(
                                row,
                                frames[candidate.FrameIndex].Buffer,
                                candidate.Row
                            )
                        )
                        {
                            definitionIndex = candidateIndex;
                            break;
                        }
                    }
                }

                if (definitionIndex < 0)
                {
                    candidates ??= [];
                    definitionIndex = rowDefinitions.Count;
                    candidates.Add(definitionIndex);
                    hashToRowDefinitionIndices[signature] = candidates;
                    var baseDefinitionIndex = lastDefinitionByRow[row - context.StartRow];
                    var startCol = context.StartCol;
                    var endColExclusive = context.EndColExclusive;
                    var depth = 0;
                    // Masking must see the complete text run; splitting it could miss
                    // a pattern that crosses the unchanged/changed boundary.
                    if (
                        maskPatterns is not { Length: > 0 }
                        && baseDefinitionIndex >= 0
                        && TryGetRowDelta(
                            buffer,
                            row,
                            frames,
                            rowDefinitions,
                            baseDefinitionIndex,
                            context,
                            out startCol,
                            out endColExclusive
                        )
                    )
                    {
                        depth = rowDefinitions[baseDefinitionIndex].Depth + 1;
                    }
                    else
                    {
                        baseDefinitionIndex = -1;
                    }

                    rowDefinitions.Add(
                        new RowDefinition(
                            frameIndex,
                            row,
                            baseDefinitionIndex,
                            startCol,
                            endColExclusive,
                            depth
                        )
                    );
                }

                rowMappings[row - context.StartRow] = definitionIndex;
                lastDefinitionByRow[row - context.StartRow] = definitionIndex;
            }
        }

        sb.Append("<defs>\n");
        for (var definitionIndex = 0; definitionIndex < rowDefinitions.Count; definitionIndex++)
        {
            var definition = rowDefinitions[definitionIndex];
            if (definition.BaseDefinitionIndex < 0)
            {
                AppendFrameGroup(
                    sb,
                    frames[definition.FrameIndex].Buffer,
                    CreateRowContext(context, definition.Row),
                    theme,
                    styles,
                    id: $"c2r{definitionIndex}",
                    @class: null,
                    opacity: opacity,
                    lengthAdjust: lengthAdjust,
                    maskPatterns: maskPatterns,
                    renderCursor: false,
                    elements: elements
                );
            }
            else
            {
                sb.Append("<g id=\"c2r");
                sb.Append(definitionIndex);
                sb.Append("\" class=\"c2 c\"><use href=\"#c2r");
                sb.Append(definition.BaseDefinitionIndex);
                sb.Append("\"/>\n");
                AppendFrameGroup(
                    sb,
                    frames[definition.FrameIndex].Buffer,
                    CreateRowRangeContext(
                        context,
                        definition.Row,
                        definition.StartCol,
                        definition.EndColExclusive
                    ),
                    theme,
                    styles,
                    id: null,
                    @class: null,
                    opacity: opacity,
                    lengthAdjust: lengthAdjust,
                    maskPatterns: null,
                    renderCursor: false,
                    applyFontClass: false,
                    overlapBaseBackground: true,
                    elements: elements
                );
                sb.Append("</g>\n");
            }
        }

        sb.Append("</defs>\n");
        return frameRowDefinitions;
    }

    private static Context CreateRowContext(in Context context, int row) =>
        new()
        {
            StartRow = row,
            EndRowExclusive = row + 1,
            StartCol = context.StartCol,
            EndColExclusive = context.EndColExclusive,
            ContentWidth = context.ContentWidth,
            ContentHeight = context.CellHeight,
            ContentOffsetX = 0d,
            ContentOffsetY = 0d,
            FontSize = context.FontSize,
            CellWidth = context.CellWidth,
            CellHeight = context.CellHeight,
            BaselineOffset = context.BaselineOffset,
        };

    private static Context CreateRowRangeContext(
        in Context context,
        int row,
        int startCol,
        int endColExclusive
    ) =>
        new()
        {
            StartRow = row,
            EndRowExclusive = row + 1,
            StartCol = startCol,
            EndColExclusive = endColExclusive,
            ContentWidth = (endColExclusive - startCol) * context.CellWidth,
            ContentHeight = context.CellHeight,
            ContentOffsetX = (startCol - context.StartCol) * context.CellWidth,
            ContentOffsetY = 0d,
            FontSize = context.FontSize,
            CellWidth = context.CellWidth,
            CellHeight = context.CellHeight,
            BaselineOffset = context.BaselineOffset,
        };

    private static bool TryGetRowDelta(
        ScreenBuffer buffer,
        int row,
        ReadOnlySpan<TerminalFrame> frames,
        List<RowDefinition> definitions,
        int baseDefinitionIndex,
        in Context context,
        out int startCol,
        out int endColExclusive
    )
    {
        // Keep both the changed span and the nested <use> chain small. This captures
        // typing and local status updates without turning every row into per-cell DOM.
        const int maxDeltaDepth = 4;
        const int maxDeltaColumns = 16;

        var baseDefinition = definitions[baseDefinitionIndex];
        startCol = context.StartCol;
        endColExclusive = context.EndColExclusive;
        if (baseDefinition.Depth >= maxDeltaDepth)
        {
            return false;
        }

        var baseBuffer = frames[baseDefinition.FrameIndex].Buffer;
        while (
            startCol < endColExclusive
            && buffer.GetCell(row, startCol).Equals(
                baseBuffer.GetCell(baseDefinition.Row, startCol)
            )
        )
        {
            startCol++;
        }

        if (startCol == endColExclusive)
        {
            return false;
        }

        while (
            endColExclusive > startCol
            && buffer.GetCell(row, endColExclusive - 1)
                .Equals(baseBuffer.GetCell(baseDefinition.Row, endColExclusive - 1))
        )
        {
            endColExclusive--;
        }

        if (
            startCol > context.StartCol
            && (
                buffer.GetCell(row, startCol).IsWideContinuation
                || baseBuffer.GetCell(baseDefinition.Row, startCol).IsWideContinuation
            )
        )
        {
            startCol--;
        }

        if (
            endColExclusive < context.EndColExclusive
            && (
                buffer.GetCell(row, endColExclusive - 1).IsWide
                || baseBuffer.GetCell(baseDefinition.Row, endColExclusive - 1).IsWide
            )
        )
        {
            endColExclusive++;
        }

        var deltaColumns = endColExclusive - startCol;
        var visibleColumns = context.EndColExclusive - context.StartCol;
        return deltaColumns <= maxDeltaColumns && deltaColumns * 4 <= visibleColumns;
    }

    private readonly record struct RowDefinition(
        int FrameIndex,
        int Row,
        int BaseDefinitionIndex,
        int StartCol,
        int EndColExclusive,
        int Depth
    );

    /// <summary>
    /// Emits independently animated row runs. Consecutive frames that reference the same
    /// row definition share one &lt;use&gt; element.
    /// </summary>
    public static void AppendAnimatedRows(
        SvgWriter sb,
        ReadOnlySpan<TerminalFrame> frames,
        int[][] frameRowDefinitions,
        in Context context,
        Theme theme,
        double totalDuration,
        double fadeOut,
        bool loop
    )
    {
        sb.Append("<g>\n");
        if (fadeOut > 0d)
        {
            var fadeStart = Math.Clamp((totalDuration - fadeOut) / totalDuration, 0d, 1d);
            sb.Append("<animate attributeName=\"opacity\" values=\"1;1;0\" keyTimes=\"0;");
            AppendKeyTime(sb, fadeStart);
            sb.Append(";1\" dur=\"");
            sb.Append(totalDuration);
            sb.Append("s\"");
            AppendSmilRepeatOrFreeze(sb, loop);
            sb.Append("/>\n");
        }

        var rowCount = context.EndRowExclusive - context.StartRow;
        for (var rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (var runStart = 0; runStart < frames.Length;)
            {
                var definitionIndex = frameRowDefinitions[runStart][rowOffset];
                var runEnd = runStart + 1;
                while (
                    runEnd < frames.Length
                    && frameRowDefinitions[runEnd][rowOffset] == definitionIndex
                )
                {
                    runEnd++;
                }

                sb.Append("<use href=\"#c2r");
                sb.Append(definitionIndex);
                sb.Append('"');
                var rowY = rowOffset * context.CellHeight;
                if (rowY != 0d)
                {
                    sb.Append(" y=\"");
                    sb.Append(rowY);
                    sb.Append('"');
                }
                AppendDisplayAnimation(
                    sb,
                    frames,
                    runStart,
                    runEnd,
                    totalDuration,
                    loop,
                    selfClosing: true
                );
                runStart = runEnd;
            }
        }

        AppendAnimatedCursors(sb, frames, context, theme, totalDuration, loop);
        sb.Append("</g>\n");
    }

    private static void AppendAnimatedCursors(
        SvgWriter sb,
        ReadOnlySpan<TerminalFrame> frames,
        in Context context,
        Theme theme,
        double totalDuration,
        bool loop
    )
    {
        for (var runStart = 0; runStart < frames.Length;)
        {
            var buffer = frames[runStart].Buffer;
            var runEnd = runStart + 1;
            while (
                runEnd < frames.Length
                && HaveSameCursor(buffer, frames[runEnd].Buffer)
            )
            {
                runEnd++;
            }

            if (buffer.CursorVisible)
            {
                sb.Append("<g");
                AppendDisplayAnimation(
                    sb,
                    frames,
                    runStart,
                    runEnd,
                    totalDuration,
                    loop,
                    selfClosing: false
                );
                RenderCursor(sb, buffer, context, theme, includeScrollback: false);
                sb.Append("</g>\n");
            }

            runStart = runEnd;
        }
    }

    private static bool HaveSameCursor(ScreenBuffer left, ScreenBuffer right) =>
        left.CursorVisible == right.CursorVisible
        && (
            !left.CursorVisible
            || (left.CursorRow == right.CursorRow && left.CursorCol == right.CursorCol)
        );

    private static void AppendDisplayAnimation(
        SvgWriter sb,
        ReadOnlySpan<TerminalFrame> frames,
        int runStart,
        int runEnd,
        double totalDuration,
        bool loop,
        bool selfClosing
    )
    {
        if (runStart == 0 && runEnd == frames.Length)
        {
            sb.Append(selfClosing ? " display=\"inline\"/>\n" : " display=\"inline\">\n");
            return;
        }

        sb.Append(" display=\"none\">\n<animate attributeName=\"display\" values=\"");
        if (runStart == 0)
        {
            sb.Append("inline;none\" keyTimes=\"0;");
            AppendKeyTime(sb, frames[runEnd].Time / totalDuration);
        }
        else if (runEnd == frames.Length)
        {
            sb.Append("none;inline\" keyTimes=\"0;");
            AppendKeyTime(sb, frames[runStart].Time / totalDuration);
        }
        else
        {
            sb.Append("none;inline;none\" keyTimes=\"0;");
            AppendKeyTime(sb, frames[runStart].Time / totalDuration);
            sb.Append(';');
            AppendKeyTime(sb, frames[runEnd].Time / totalDuration);
        }
        sb.Append("\" dur=\"");
        sb.Append(totalDuration);
        sb.Append("s\" calcMode=\"discrete\"");
        AppendSmilRepeatOrFreeze(sb, loop);
        sb.Append("/>\n");
        if (selfClosing)
        {
            sb.Append("</use>\n");
        }
    }

    private static void AppendKeyTime(SvgWriter sb, double keyTime)
    {
        sb.Append(
            Math.Clamp(keyTime, 0d, 1d)
                .ToString("0.######", CultureInfo.InvariantCulture)
        );
    }

    private static void AppendSmilRepeatOrFreeze(SvgWriter sb, bool loop)
    {
        if (loop)
        {
            sb.Append(" repeatCount=\"indefinite\"");
        }
        else
        {
            sb.Append(" fill=\"freeze\"");
        }
    }
}
