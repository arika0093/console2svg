using System;
using System.Collections.Generic;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
{
    private static IReadOnlyList<TerminalFrame> SpreadCollapsedFrameTimes(
        IReadOnlyList<TerminalFrame> frames,
        double maxFps
    )
    {
        if (frames.Count <= 1)
        {
            return frames;
        }

        List<TerminalFrame>? adjusted = null;
        for (var runStart = 0; runStart < frames.Count;)
        {
            var runEnd = runStart;
            while (
                runEnd + 1 < frames.Count
                && HaveSameTime(frames[runEnd + 1].Time, frames[runStart].Time)
            )
            {
                runEnd++;
            }

            if (runEnd == runStart)
            {
                if (adjusted != null)
                {
                    adjusted.Add(frames[runStart]);
                }

                runStart++;
                continue;
            }

            adjusted ??= new List<TerminalFrame>(frames.Count);
            if (adjusted.Count == 0)
            {
                for (var copyIndex = 0; copyIndex < runStart; copyIndex++)
                {
                    adjusted.Add(frames[copyIndex]);
                }
            }

            var runCount = runEnd - runStart + 1;
            if (runStart == 0 && frames[runStart].Time <= 0d)
            {
                var upperBound =
                    runEnd + 1 < frames.Count
                        ? frames[runEnd + 1].Time
                        : GetMinimumFrameInterval(maxFps);
                if (upperBound <= 0d)
                {
                    upperBound = GetMinimumFrameInterval(maxFps);
                }

                var step = upperBound / runCount;
                for (var offset = 0; offset < runCount; offset++)
                {
                    adjusted.Add(
                        new TerminalFrame(step * offset, frames[runStart + offset].Buffer)
                    );
                }
            }
            else
            {
                var lowerBound = runStart > 0 ? adjusted[runStart - 1].Time : 0d;
                var upperBound = frames[runStart].Time;
                var step = (upperBound - lowerBound) / runCount;
                if (step <= 0d)
                {
                    step = GetMinimumFrameInterval(maxFps) / runCount;
                }

                for (var offset = 0; offset < runCount; offset++)
                {
                    adjusted.Add(
                        new TerminalFrame(
                            lowerBound + (step * (offset + 1)),
                            frames[runStart + offset].Buffer
                        )
                    );
                }
            }

            runStart = runEnd + 1;
        }

        return adjusted ?? frames;
    }

    private static double GetFinalFrameHoldDuration(
        double videoSleep,
        double fadeOut,
        double maxFps
    )
    {
        if (videoSleep > 0d || fadeOut > 0d)
        {
            return videoSleep;
        }

        return GetMinimumFrameInterval(maxFps);
    }

}
