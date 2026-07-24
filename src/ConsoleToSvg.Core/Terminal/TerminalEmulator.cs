using System.Collections.Generic;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Terminal;

public sealed class TerminalEmulator
{
    private readonly AnsiParser _parser;

    public TerminalEmulator(int width, int height, Theme theme)
    {
        Theme = theme;
        Buffer = new ScreenBuffer(width, height, theme);
        _parser = new AnsiParser(Buffer, theme);
    }

    public Theme Theme { get; }

    public ScreenBuffer Buffer { get; }

    public void Process(string text)
    {
        _parser.Process(text);
    }

    public ScreenBuffer Replay(RecordingSession session, int frameIndex)
    {
        var upper = frameIndex;
        if (upper >= session.Events.Count)
        {
            upper = session.Events.Count - 1;
        }

        if (upper < 0)
        {
            return Buffer;
        }

        for (var i = 0; i <= upper; i++)
        {
            Process(session.Events[i].Data);
        }

        return Buffer;
    }

    public IReadOnlyList<TerminalFrame> ReplayFrames(RecordingSession session)
    {
        var frames = new List<TerminalFrame>();
        for (var i = 0; i < session.Events.Count; i++)
        {
            Process(session.Events[i].Data);
            frames.Add(new TerminalFrame(session.Events[i].Time, Buffer.Clone()));
        }

        return frames;
    }
}

public sealed class TerminalFrame
{
    public TerminalFrame(double time, ScreenBuffer buffer)
    {
        Time = time;
        Buffer = buffer;
    }

    public double Time { get; }

    public ScreenBuffer Buffer { get; }
}
