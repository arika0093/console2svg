using System;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
{
    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> TrimTrailingAltScreenRestoreFrame(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        RecordingSession session
    )
    {
        if (frames.Count <= 1 || frames[0].EventIndex < 0)
        {
            return frames;
        }

        var lastNonBlankIndex = -1;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (!SvgRenderShared.IsBlankFrame(frames[i].Buffer))
            {
                lastNonBlankIndex = i;
                break;
            }
        }

        if (lastNonBlankIndex < 0 || lastNonBlankIndex == frames.Count - 1)
        {
            return frames;
        }

        if (
            !SvgRenderShared.HasTrailingBlankIndicators(
                session,
                frames[lastNonBlankIndex].EventIndex + 1
            )
        )
        {
            return frames;
        }

        var trimmed = new System.Collections.Generic.List<TerminalFrame>(lastNonBlankIndex + 1);
        for (var keep = 0; keep <= lastNonBlankIndex; keep++)
        {
            trimmed.Add(frames[keep]);
        }

        return trimmed;
    }

    // Blank/trailing-frame detection moved to SvgRenderShared.

    private static bool HaveSameTime(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }

    private static double GetMinimumFrameInterval(double maxFps)
    {
        return maxFps > 0d ? 1d / maxFps : 0.05d;
    }

}
