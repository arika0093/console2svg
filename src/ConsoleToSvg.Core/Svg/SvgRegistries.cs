using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

internal sealed class SvgStyleRegistry
{
    private readonly Dictionary<TextStyleKey, string> _classes = new();
    private readonly List<TextStyleKey> _styles = [];

    public void CollectCellStyle(in ScreenCell cell, string effectiveForeground)
    {
        GetTextClass(
            effectiveForeground,
            cell.Bold,
            cell.Italic,
            cell.Underline,
            cell.Strikethrough,
            cell.Overline,
            cell.UnderlineColor
        );
    }

    public string GetTextClass(
        string foreground,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        bool strikethrough = false,
        bool overline = false,
        string? underlineColor = null
    )
    {
        var key = new TextStyleKey(
            foreground,
            bold,
            italic,
            underline,
            strikethrough,
            overline,
            underlineColor
        );
        ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _classes,
            key,
            out var exists
        );
        if (exists)
        {
            return existing!;
        }

        var className = GetClassName(_styles.Count);
        existing = className;
        _styles.Add(key);
        return className;
    }

    public void AppendCss(SvgWriter sb)
    {
        sb.Append('\n');
        for (var i = 0; i < _styles.Count; i++)
        {
            var style = _styles[i];
            sb.Append(".c2 .");
            sb.Append(GetClassName(i));
            sb.Append("{fill:");
            sb.Append(style.Foreground);
            if (style.Bold)
            {
                sb.Append(";font-weight:bold");
            }
            if (style.Italic)
            {
                sb.Append(";font-style:italic");
            }
            if (style.Underline || style.Strikethrough || style.Overline)
            {
                sb.Append(";text-decoration:");
                var separator = "";
                if (style.Underline)
                {
                    sb.Append("underline");
                    separator = " ";
                }
                if (style.Strikethrough)
                {
                    sb.Append(separator);
                    sb.Append("line-through");
                    separator = " ";
                }
                if (style.Overline)
                {
                    sb.Append(separator);
                    sb.Append("overline");
                }
            }
            if (style.UnderlineColor is not null)
            {
                sb.Append(";text-decoration-color:");
                sb.Append(style.UnderlineColor);
            }
            sb.Append("}\n");
        }
    }

    private static string GetClassName(int index)
    {
        index += 26;
        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        do
        {
            buffer[--position] = (char)('a' + index % 26);
            index = index / 26 - 1;
        } while (index >= 0);

        return new string(buffer[position..]);
    }

    private readonly record struct TextStyleKey(
        string Foreground,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        bool Overline,
        string? UnderlineColor
    );
}

internal sealed class SvgElementRegistry
{
    private readonly Dictionary<ElementKey, int> _elements = new();

    public void AppendRect(
        SvgWriter sb,
        string? @class,
        double? x,
        double? y,
        double width,
        double height,
        string fill,
        string? stroke = null,
        double? strokeWidth = null,
        bool nonScalingStroke = false
    )
    {
        var key = new ElementKey(
            ElementType.Rect,
            @class,
            NormalizePosition(x),
            NormalizePosition(y),
            width,
            height,
            fill,
            stroke,
            strokeWidth,
            nonScalingStroke,
            null
        );
        if (TryAppendUseOrAdd(sb, key, out var id))
        {
            return;
        }

        sb.Append("<rect");
        if (!string.IsNullOrWhiteSpace(@class))
        {
            sb.Append(" class=\"");
            sb.Append(@class);
            sb.Append('"');
        }
        AppendPosition(sb, " x=\"", x);
        AppendPosition(sb, " y=\"", y);
        sb.Append(" width=\"");
        sb.Append(width);
        sb.Append("\" height=\"");
        sb.Append(height);
        sb.Append("\" id=\"c2e");
        sb.Append(id);
        sb.Append("\" fill=\"");
        sb.Append(fill);
        if (stroke is not null)
        {
            sb.Append("\" stroke=\"");
            sb.Append(stroke);
        }
        if (strokeWidth is not null)
        {
            sb.Append("\" stroke-width=\"");
            sb.Append(strokeWidth.Value);
        }
        if (nonScalingStroke)
        {
            sb.Append("\" vector-effect=\"non-scaling-stroke");
        }
        sb.Append("\"/>\n");
    }

    public void AppendPath(SvgWriter sb, string pathData, string fill)
    {
        var key = new ElementKey(
            ElementType.Path,
            null,
            null,
            null,
            0d,
            0d,
            fill,
            null,
            null,
            false,
            pathData
        );
        if (TryAppendUseOrAdd(sb, key, out var id))
        {
            return;
        }

        sb.Append("<path d=\"");
        sb.Append(pathData);
        sb.Append("\" id=\"c2e");
        sb.Append(id);
        sb.Append("\" fill=\"");
        sb.Append(fill);
        sb.Append("\"/>\n");
    }

    private bool TryAppendUseOrAdd(SvgWriter sb, in ElementKey key, out int id)
    {
        ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _elements,
            key,
            out var exists
        );
        if (!exists)
        {
            id = _elements.Count - 1;
            existing = id;
            return false;
        }

        id = existing;
        sb.Append("<use href=\"#c2e");
        sb.Append(id);
        sb.Append("\"/>\n");
        return true;
    }

    private static double? NormalizePosition(double? value) =>
        value is null || value.Value == 0d ? null : value;

    private static void AppendPosition(SvgWriter sb, string attribute, double? value)
    {
        if (value is null || value.Value == 0d)
        {
            return;
        }

        sb.Append(attribute);
        sb.Append(value.Value);
        sb.Append('"');
    }

    private enum ElementType : byte
    {
        Rect,
        Path,
    }

    private readonly record struct ElementKey(
        ElementType Type,
        string? Class,
        double? X,
        double? Y,
        double Width,
        double Height,
        string Fill,
        string? Stroke,
        double? StrokeWidth,
        bool NonScalingStroke,
        string? PathData
    );
}
