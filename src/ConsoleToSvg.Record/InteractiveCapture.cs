using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Recording;

public sealed class InteractiveCapture
{
    public InteractiveCapture(ScreenBuffer screen)
    {
        Screen = screen;
        Frames = [];
    }

    public InteractiveCapture(IReadOnlyList<TerminalFrame> frames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        Frames = frames;
        Screen = frames[0].Buffer;
    }

    public ScreenBuffer Screen { get; }

    public IReadOnlyList<TerminalFrame> Frames { get; }

    public bool IsVideo => Frames.Count > 0;
}
