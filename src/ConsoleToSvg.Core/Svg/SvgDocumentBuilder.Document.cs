using System;
using System.Globalization;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal static partial class SvgDocumentBuilder
{
    public static void BeginSvg(
        StringBuilder sb,
        Context context,
        Theme theme,
        string? additionalCss,
        string? font = null,
        ChromeDefinition? chrome = null,
        string? commandHeader = null,
        double opacity = 1d,
        string[]? background = null
    )
    {
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        sb.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\" ");
        sb.Append("width=\"");
        sb.Append(Format(context.OutputWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.OutputHeight));
        sb.Append("\" ");
        sb.Append("viewBox=\"");
        sb.Append(Format(context.ViewBoxX));
        sb.Append(' ');
        sb.Append(Format(context.ViewBoxY));
        sb.Append(' ');
        sb.Append(Format(context.ViewBoxWidth));
        sb.Append(' ');
        sb.Append(Format(context.ViewBoxHeight));
        sb.Append("\" role=\"img\" aria-label=\"console2svg output\">\n");

        var effectiveFont = string.IsNullOrWhiteSpace(font)
            ? DefaultFontFamily
            : EscapeAttribute(font);
        sb.Append("<style>\n");
        sb.Append(".crt {\n");
        sb.Append("  font-family: ");
        sb.Append(effectiveFont);
        sb.Append(";\n");
        sb.Append("  font-size: ");
        sb.Append(Format(context.FontSize));
        sb.Append("px;\n");
        sb.Append("}\n");
        sb.Append(".blink { animation: blink 1s step-start infinite; }\n");
        sb.Append("@keyframes blink { 50% { visibility: hidden; } }\n");
        sb.Append("text {\n");
        sb.Append("  dominant-baseline: alphabetic;\n");
        sb.Append("}\n");
        sb.Append(".bg {\n");
        sb.Append("  shape-rendering: crispEdges;\n");
        sb.Append("}\n");
        if (!string.IsNullOrWhiteSpace(additionalCss))
        {
            sb.Append('\n');
            sb.Append(additionalCss);
            if (!additionalCss.EndsWith('\n'))
            {
                sb.Append('\n');
            }
        }

        sb.Append("</style>\n");

        AppendDefs(sb, context, chrome, background);
        AppendBackground(sb, context, chrome, background);
        AppendGroupOpen(sb, opacity);
        AppendChrome(sb, context, theme, chrome);
        AppendClientBackground(sb, context, theme, chrome);
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

    /// <summary>Renders the always-opaque background layer (desktop bg for desktop styles, canvas bg otherwise).</summary>
    private static void AppendBackground(
        StringBuilder sb,
        Context context,
        ChromeDefinition? chrome,
        string[]? background = null
    )
    {
        if (chrome?.IsDesktop == true)
        {
            // Desktop background only  Eshadow + chrome go in AppendChrome (inside the single opacity group)
            sb.Append("<rect ");
            if (context.HasViewBoxOffset)
            {
                sb.Append("x=\"");
                sb.Append(Format(context.ViewBoxX));
                sb.Append("\" y=\"");
                sb.Append(Format(context.ViewBoxY));
                sb.Append("\" ");
            }

            sb.Append("width=\"");
            sb.Append(Format(context.ViewBoxWidth));
            sb.Append("\" height=\"");
            sb.Append(Format(context.ViewBoxHeight));
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
        StringBuilder sb,
        Context context,
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
        StringBuilder sb,
        Context context,
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

        sb.Append("<rect x=\"");
        sb.Append(Format(left));
        sb.Append("\" y=\"");
        sb.Append(Format(top));
        sb.Append("\" width=\"");
        sb.Append(Format(width));
        sb.Append("\" height=\"");
        sb.Append(Format(height));
        if (chrome is { ClientCornerRadius: > 0 })
        {
            sb.Append("\" rx=\"");
            sb.Append(Format(chrome.ClientCornerRadius));
            sb.Append("\" ry=\"");
            sb.Append(Format(chrome.ClientCornerRadius));
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
        StringBuilder sb,
        Context context,
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
            fill = "url(#desktop-bg)"; // gradient / image
        else if (chrome != null)
            fill = null; // chrome window rect provides the background fill
        // else no chrome and no --background: omit rect ・transparent canvas

        if (fill == null)
            return;

        sb.Append("<rect ");
        if (context.HasViewBoxOffset)
        {
            sb.Append("x=\"");
            sb.Append(Format(context.ViewBoxX));
            sb.Append("\" y=\"");
            sb.Append(Format(context.ViewBoxY));
            sb.Append("\" ");
        }

        sb.Append("width=\"");
        sb.Append(Format(context.ViewBoxWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ViewBoxHeight));
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
        // gradient (2 colors), image, or default ↁEreference defs
        return "url(#desktop-bg)";
    }

    /// <summary>Emits SVG &lt;defs&gt; containing gradient or image background definitions if needed.</summary>
    private static void AppendDefs(
        StringBuilder sb,
        Context context,
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
            AppendLinearGradientDef(sb, "desktop-bg", background[0], background[1]);
        }
        else
        {
            // Default gradient from chrome definition  Esubtle diagonal
            var c1 = chrome?.DesktopGradientFrom ?? "#1a1d2e";
            var c2 = chrome?.DesktopGradientTo ?? "#252840";
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

    private static void AppendImagePatternDef(StringBuilder sb, string imagePath, Context context)
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
            "<pattern id=\"desktop-bg\" patternUnits=\"userSpaceOnUse\" patternContentUnits=\"userSpaceOnUse\" x=\""
        );
        sb.Append(Format(context.ViewBoxX));
        sb.Append("\" y=\"");
        sb.Append(Format(context.ViewBoxY));
        sb.Append("\" width=\"");
        sb.Append(Format(context.ViewBoxWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ViewBoxHeight));
        sb.Append("\">");
        sb.Append("<image href=\"");
        sb.Append(EscapeAttribute(href));
        sb.Append("\" x=\"");
        sb.Append("0");
        sb.Append("\" y=\"");
        sb.Append("0");
        sb.Append("\" width=\"");
        sb.Append(Format(context.ViewBoxWidth));
        sb.Append("\" height=\"");
        sb.Append(Format(context.ViewBoxHeight));
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

    public static void EndSvg(StringBuilder sb, double opacity = 1d)
    {
        AppendGroupClose(sb, opacity);
        sb.Append("</svg>");
    }

    /// <summary>
    /// Renders unique frame contents into a &lt;defs&gt; block so they can be referenced
    /// by &lt;use&gt; elements emitted by <see cref="AppendFrameUse"/>. Each unique frame is stored
    /// as <c>&lt;g id="fd-{frameIndex}"&gt;</c> with no animation class.
    /// </summary>
    public static void AppendFrameDefs(
        StringBuilder sb,
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        System.Collections.Generic.IReadOnlyList<int> uniqueFrameIndices,
        Context context,
        Theme theme,
        string lengthAdjust,
        double opacity = 1d
    )
    {
        sb.Append("<defs>\n");
        foreach (var fi in uniqueFrameIndices)
        {
            AppendFrameGroup(
                sb,
                frames[fi].Buffer,
                context,
                theme,
                id: $"fd-{fi}",
                @class: null,
                opacity: opacity,
                lengthAdjust: lengthAdjust
            );
        }

        sb.Append("</defs>\n");
    }

    /// <summary>
    /// Emits a &lt;use&gt; element that references a unique frame stored in &lt;defs&gt; by
    /// <see cref="AppendFrameDefs"/>. The element carries the per-frame animation CSS class.
    /// </summary>
    public static void AppendFrameUse(
        StringBuilder sb,
        string defsId,
        string frameId,
        string frameClass
    )
    {
        sb.Append("<use href=\"#");
        sb.Append(EscapeAttribute(defsId));
        sb.Append("\" id=\"");
        sb.Append(EscapeAttribute(frameId));
        sb.Append("\" class=\"");
        sb.Append(EscapeAttribute(frameClass));
        sb.Append("\"/>\n");
    }
}
