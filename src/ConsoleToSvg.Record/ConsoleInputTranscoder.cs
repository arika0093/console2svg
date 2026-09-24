using System;
using System.Text;

namespace ConsoleToSvg.Recording;

internal sealed class ConsoleInputTranscoder
{
    private readonly Decoder _decoder;
    private readonly char[] _characters = new char[1024];

    public ConsoleInputTranscoder(Encoding sourceEncoding)
    {
        _decoder = sourceEncoding.GetDecoder();
    }

    public byte[] Transcode(ReadOnlySpan<byte> input)
    {
        var charCount = _decoder.GetChars(input, _characters, flush: false);
        return charCount == 0 ? [] : Encoding.UTF8.GetBytes(_characters, 0, charCount);
    }
}
