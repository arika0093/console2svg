using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleToSvg.Svg;

/// <remarks>
/// Dynamic SVG fragments intentionally use separate appends on hot paths. This type has
/// no interpolated-string handler, so interpolation allocates the completed string before
/// writing, while the numeric overloads format directly into the destination writer.
/// </remarks>
internal sealed class SvgWriter
{
    private readonly TextWriter _writer;

    public SvgWriter(TextWriter writer)
    {
        _writer = writer;
    }

    public SvgWriter Append(string? value)
    {
        _writer.Write(value);
        return this;
    }

    public SvgWriter Append(char value)
    {
        _writer.Write(value);
        return this;
    }

    public SvgWriter Append(double value)
    {
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out var charsWritten, "0.###", CultureInfo.InvariantCulture))
        {
            _writer.Write(value.ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        _writer.Write(buffer[..charsWritten]);
        return this;
    }

    public SvgWriter Append(int value)
    {
        Span<char> buffer = stackalloc char[11];
        if (!value.TryFormat(buffer, out var charsWritten, provider: CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Unable to format SVG integer value.");
        }

        _writer.Write(buffer[..charsWritten]);
        return this;
    }

    public SvgWriter Append(StringBuilder value)
    {
        foreach (var chunk in value.GetChunks())
        {
            _writer.Write(chunk.Span);
        }

        return this;
    }
}
