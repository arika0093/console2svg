using System;
using System.Collections.Generic;

namespace ConsoleToSvg.Recording;

public sealed class AsciicastHeader
{
    public int version { get; set; } = 2;

    public int width { get; set; }

    public int height { get; set; }

    public long timestamp { get; set; }
}

public sealed class AsciicastEvent
{
    public double Time { get; set; }

    public string Type { get; set; } = "o";

    public string Data { get; set; } = string.Empty;
}

public sealed class RecordingSession
{
    public RecordingSession(int width, int height)
    {
        Header = new AsciicastHeader
        {
            width = width,
            height = height,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Events = new List<AsciicastEvent>();
    }

    public RecordingSession(AsciicastHeader header)
    {
        Header = header;
        Events = new List<AsciicastEvent>();
    }

    public AsciicastHeader Header { get; }

    public List<AsciicastEvent> Events { get; }

    public void AddEvent(double timeSeconds, string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        Events.Add(
            new AsciicastEvent
            {
                Time = timeSeconds,
                Type = "o",
                Data = data,
            }
        );
    }
}
