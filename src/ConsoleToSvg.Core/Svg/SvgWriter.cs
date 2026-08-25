using System.IO;
using System.Text;

namespace ConsoleToSvg.Svg;

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

    public SvgWriter Append(StringBuilder value)
    {
        foreach (var chunk in value.GetChunks())
        {
            _writer.Write(chunk.Span);
        }

        return this;
    }
}
