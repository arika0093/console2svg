using System.Text;

namespace ConsoleToSvg.Utils;

internal sealed class LfStringBuilder
{
    private readonly StringBuilder _builder;

    public LfStringBuilder()
        : this(0)
    {
    }

    public LfStringBuilder(int capacity)
    {
        _builder = capacity > 0 ? new StringBuilder(capacity) : new StringBuilder();
    }

    public StringBuilder Inner => _builder;

    public LfStringBuilder Append(string? value)
    {
        _builder.Append(value);
        return this;
    }

    public LfStringBuilder Append(char value)
    {
        _builder.Append(value);
        return this;
    }

    public LfStringBuilder AppendLine()
    {
        _builder.Append('\n');
        return this;
    }

    public LfStringBuilder AppendLine(string? value)
    {
        _builder.Append(value);
        _builder.Append('\n');
        return this;
    }

    public override string ToString()
    {
        return _builder.ToString();
    }
}
