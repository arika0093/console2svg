using System;
using System.Globalization;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using ConsoleToSvg.Utils;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
{
    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> SpreadCollapsedFrameTimes(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double maxFps
    )
    {
        if (frames.Count <= 1)
        {
            return frames;
        }

        System.Collections.Generic.List<TerminalFrame>? adjusted = null;
        for (var runStart = 0; runStart < frames.Count; )
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

            adjusted ??= new System.Collections.Generic.List<TerminalFrame>(frames.Count);
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

    private static ulong BuildVisualSignature(ScreenBuffer buffer)
    {
        const ulong fnvOffset = 1469598103934665603UL;

        var signature = fnvOffset;
        signature = HashInt(signature, buffer.CursorRow);
        signature = HashInt(signature, buffer.CursorCol);

        for (var row = 0; row < buffer.Height; row++)
        {
            for (var col = 0; col < buffer.Width; col++)
            {
                var cell = buffer.GetCell(row, col);
                signature = HashString(signature, cell.Text);
                signature = HashString(signature, cell.Foreground);
                signature = HashString(signature, cell.Background);
                signature = HashBool(signature, cell.Bold);
                signature = HashBool(signature, cell.Italic);
                signature = HashBool(signature, cell.Underline);
                signature = HashBool(signature, cell.Reversed);
                signature = HashBool(signature, cell.Faint);
                signature = HashBool(signature, cell.Hidden);
                signature = HashBool(signature, cell.Strikethrough);
                signature = HashBool(signature, cell.Overline);
                signature = HashBool(signature, cell.Blink);
                signature = HashString(signature, cell.UnderlineColor ?? string.Empty);
                signature = HashBool(signature, cell.IsWide);
                signature = HashBool(signature, cell.IsWideContinuation);
            }
        }

        return signature;
    }

    private static ulong HashString(ulong signature, string value)
    {
        const ulong fnvPrime = 1099511628211UL;
        if (value is null)
        {
            signature ^= 0;
            signature *= fnvPrime;
            return signature;
        }

        for (var i = 0; i < value.Length; i++)
        {
            signature ^= value[i];
            signature *= fnvPrime;
        }

        signature ^= 0xFF;
        signature *= fnvPrime;
        return signature;
    }

    private static ulong HashBool(ulong signature, bool value)
    {
        const ulong fnvPrime = 1099511628211UL;
        signature ^= value ? (byte)1 : (byte)0;
        signature *= fnvPrime;
        return signature;
    }

    private static ulong HashInt(ulong signature, int value)
    {
        const ulong fnvPrime = 1099511628211UL;
        unchecked
        {
            signature ^= (byte)value;
            signature *= fnvPrime;
            signature ^= (byte)(value >> 8);
            signature *= fnvPrime;
            signature ^= (byte)(value >> 16);
            signature *= fnvPrime;
            signature ^= (byte)(value >> 24);
            signature *= fnvPrime;
        }

        return signature;
    }
}
