using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Defines the window chrome (frame decoration) rendered around the terminal content.
/// Supports a simple SVG template engine with positional variables and arithmetic.
/// </summary>
public sealed class ChromeDefinition
{
    /// <summary>Chrome inset at the top (e.g. title bar height in pixels).</summary>
    [JsonPropertyName("paddingTop")]
    public double PaddingTop { get; set; }

    /// <summary>Chrome inset at the right (border width in pixels).</summary>
    [JsonPropertyName("paddingRight")]
    public double PaddingRight { get; set; }

    /// <summary>Chrome inset at the bottom (border width in pixels).</summary>
    [JsonPropertyName("paddingBottom")]
    public double PaddingBottom { get; set; }

    /// <summary>Chrome inset at the left (border width in pixels).</summary>
    [JsonPropertyName("paddingLeft")]
    public double PaddingLeft { get; set; }

    /// <summary>
    /// When true, the chrome is rendered as a floating window on a desktop background.
    /// Adds outer desktop padding and a drop shadow offset.
    /// </summary>
    [JsonPropertyName("isDesktop")]
    public bool IsDesktop { get; set; }

    /// <summary>Outer desktop padding in pixels on each side when <see cref="IsDesktop"/> is true.</summary>
    [JsonPropertyName("desktopPadding")]
    public double DesktopPadding { get; set; } = 20d;

    /// <summary>Drop shadow offset in pixels when <see cref="IsDesktop"/> is true.</summary>
    [JsonPropertyName("shadowOffset")]
    public double ShadowOffset { get; set; } = 6d;

    /// <summary>Default desktop background gradient start color (e.g. "#1a1d2e"). null = use fallback default.</summary>
    [JsonPropertyName("desktopGradientFrom")]
    public string? DesktopGradientFrom { get; set; }

    /// <summary>Default desktop background gradient end color (e.g. "#252840"). null = use fallback default.</summary>
    [JsonPropertyName("desktopGradientTo")]
    public string? DesktopGradientTo { get; set; }

    /// <summary>
    /// Override the terminal theme background color (e.g. "#0c0c0c" for Windows Terminal).
    /// null = use the standard theme background.
    /// </summary>
    [JsonPropertyName("themeBackgroundOverride")]
    public string? ThemeBackgroundOverride { get; set; }

    /// <summary>
    /// Corner radius for the client background rect inside the chrome.
    /// 0 = square corners.
    /// </summary>
    [JsonPropertyName("clientCornerRadius")]
    public double ClientCornerRadius { get; set; }

    /// <summary>
    /// SVG fragment template for the chrome elements.
    /// Supports template variables with optional arithmetic offset:
    ///   {winX}, {winY}       — top-left corner of the window area
    ///   {winW}, {winH}       — width and height of the window area
    ///   {winRight}           — winX + winW (right edge)
    ///   {winBottom}          — winY + winH (bottom edge)
    ///   {totalW}, {totalH}   — total SVG canvas size
    ///   {bg}                 — terminal theme background color string
    /// Arithmetic: {winX+14}, {winRight-23}, {winW-1}, {winY+0.5}, etc.
    /// </summary>
    [JsonPropertyName("svgTemplate")]
    public string SvgTemplate { get; set; } = string.Empty;

    /// <summary>
    /// SVG fragment template used instead of <see cref="SvgTemplate"/> when <see cref="IsDesktop"/> is true.
    /// Typically includes a drop shadow rect before the base chrome elements.
    /// Falls back to <see cref="SvgTemplate"/> when empty.
    /// </summary>
    [JsonPropertyName("desktopSvgTemplate")]
    public string? DesktopSvgTemplate { get; set; }

    /// <summary>
    /// Renders the chrome SVG by substituting template variables.
    /// </summary>
    internal string Render(
        double winX,
        double winY,
        double winW,
        double winH,
        double totalW,
        double totalH,
        string themeBackground
    )
    {
        var template =
            IsDesktop && !string.IsNullOrEmpty(DesktopSvgTemplate)
                ? DesktopSvgTemplate
                : SvgTemplate;

        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(template.Length + 256);
        var i = 0;

        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            if (open < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            sb.Append(template, i, open - i);
            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                sb.Append(template, open, template.Length - open);
                break;
            }

            AppendEvaluatedExpr(
                sb,
                template.AsSpan(open + 1, close - open - 1),
                winX,
                winY,
                winW,
                winH,
                totalW,
                totalH,
                themeBackground
            );
            i = close + 1;
        }

        return sb.ToString();
    }

    private static void AppendEvaluatedExpr(
        StringBuilder output,
        ReadOnlySpan<char> expr,
        double winX,
        double winY,
        double winW,
        double winH,
        double totalW,
        double totalH,
        string themeBackground
    )
    {
        // {bg} expands to the theme background color string
        if (expr.Equals("bg", StringComparison.OrdinalIgnoreCase))
        {
            output.Append(themeBackground);
            return;
        }

        // Parse "varName" or "varName+offset" or "varName-offset"
        var opIdx = -1;
        for (var j = 1; j < expr.Length; j++)
        {
            if (expr[j] == '+' || expr[j] == '-')
            {
                opIdx = j;
                break;
            }
        }

        ReadOnlySpan<char> varName;
        double offset = 0d;

        if (opIdx >= 0)
        {
            varName = expr[..opIdx];
            var offsetText = expr[opIdx..];
            if (
                !double.TryParse(
                    offsetText,
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out offset
                )
            )
            {
                AppendUnknownExpr(output, expr);
                return;
            }
        }
        else
        {
            varName = expr;
        }

        var baseVal = ResolveBaseValue(
            varName,
            winX,
            winY,
            winW,
            winH,
            totalW,
            totalH
        );

        if (double.IsNaN(baseVal))
        {
            AppendUnknownExpr(output, expr);
            return;
        }

        output.Append(
            CultureInfo.InvariantCulture,
            $"{baseVal + offset:0.###}"
        );
    }

    private static double ResolveBaseValue(
        ReadOnlySpan<char> name,
        double winX,
        double winY,
        double winW,
        double winH,
        double totalW,
        double totalH
    )
    {
        if (name.SequenceEqual("winX"))
            return winX;
        if (name.SequenceEqual("winY"))
            return winY;
        if (name.SequenceEqual("winW"))
            return winW;
        if (name.SequenceEqual("winH"))
            return winH;
        if (name.SequenceEqual("winRight"))
            return winX + winW;
        if (name.SequenceEqual("winBottom"))
            return winY + winH;
        if (name.SequenceEqual("totalW"))
            return totalW;
        if (name.SequenceEqual("totalH"))
            return totalH;
        return double.NaN;
    }

    private static void AppendUnknownExpr(StringBuilder output, ReadOnlySpan<char> expr)
    {
        output.Append('{');
        output.Append(expr);
        output.Append('}');
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ChromeDefinition))]
internal sealed partial class ChromeDefinitionJsonContext : JsonSerializerContext { }
