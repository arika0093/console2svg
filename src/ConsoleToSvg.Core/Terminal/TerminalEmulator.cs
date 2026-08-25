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
            frames.Add(new TerminalFrame(session.Events[i].Time, Buffer.Clone(), i));
        }

        return frames;
    }

    internal IReadOnlyList<TerminalFrame> ReplayFrames(
        RecordingSession session,
        double maxFps,
        System.Func<double, double> normalizeTime
    )
    {
        if (session.Events.Count == 0)
        {
            return [];
        }

        var minimumInterval = maxFps > 0 ? 1d / maxFps : 0d;
        var frames = new List<TerminalFrame>();
        ScreenBuffer? pendingBuffer = null;
        var pendingTime = 0d;
        var pendingEventIndex = -1;
        var pendingSignature = 0UL;
        var lastKeptTime = 0d;
        var lastKeptSignature = 0UL;

        for (var i = 0; i < session.Events.Count; i++)
        {
            Process(session.Events[i].Data);
            var time = normalizeTime(session.Events[i].Time);

            if (i == 0)
            {
                var firstBuffer = Buffer.Clone();
                frames.Add(new TerminalFrame(time, firstBuffer, i));
                lastKeptTime = time;
                lastKeptSignature = firstBuffer.GetVisualSignature();
                continue;
            }

            if (i == session.Events.Count - 1)
            {
                if (pendingBuffer is not null)
                {
                    frames.Add(
                        new TerminalFrame(pendingTime, pendingBuffer, pendingEventIndex)
                    );
                }

                frames.Add(new TerminalFrame(time, Buffer.Clone(), i));
                continue;
            }

            var signature = Buffer.GetVisualSignature();
            var visualChanged = signature != lastKeptSignature;
            var elapsed = time - lastKeptTime;

            if (minimumInterval > 0 && elapsed < minimumInterval)
            {
                if (visualChanged)
                {
                    pendingBuffer ??= Buffer.Clone();
                    if (pendingEventIndex >= 0)
                    {
                        pendingBuffer.CopyVisibleStateFrom(Buffer);
                    }

                    pendingTime = time;
                    pendingEventIndex = i;
                    pendingSignature = signature;
                }
                else
                {
                    pendingBuffer = null;
                    pendingEventIndex = -1;
                    frames[frames.Count - 1].EventIndex = i;
                }
                continue;
            }

            if (pendingBuffer is not null)
            {
                frames.Add(new TerminalFrame(pendingTime, pendingBuffer, pendingEventIndex));
                lastKeptTime = pendingTime;
                lastKeptSignature = pendingSignature;
                pendingBuffer = null;
                pendingEventIndex = -1;

                visualChanged = signature != lastKeptSignature;
                elapsed = time - lastKeptTime;
                if (minimumInterval > 0 && elapsed < minimumInterval)
                {
                    if (visualChanged)
                    {
                        pendingBuffer = Buffer.Clone();
                        pendingTime = time;
                        pendingEventIndex = i;
                        pendingSignature = signature;
                    }
                    else
                    {
                        frames[frames.Count - 1].EventIndex = i;
                    }
                    continue;
                }

                if (!visualChanged)
                {
                    frames[frames.Count - 1].EventIndex = i;
                    continue;
                }
            }

            var retainedBuffer = Buffer.Clone();
            frames.Add(new TerminalFrame(time, retainedBuffer, i));
            lastKeptTime = time;
            lastKeptSignature = signature;
        }

        return frames;
    }
}

public sealed class TerminalFrame
{
    public TerminalFrame(double time, ScreenBuffer buffer)
        : this(time, buffer, -1)
    {
    }

    internal TerminalFrame(double time, ScreenBuffer buffer, int eventIndex)
    {
        Time = time;
        Buffer = buffer;
        EventIndex = eventIndex;
    }

    public double Time { get; }

    public ScreenBuffer Buffer { get; }

    internal int EventIndex { get; set; }
}
