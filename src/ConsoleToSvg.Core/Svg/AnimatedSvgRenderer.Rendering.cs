using System;
using System.Globalization;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using ConsoleToSvg.Utils;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
{
    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> TrimTrailingAltScreenRestoreFrame(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        RecordingSession session
    )
    {
        if (frames.Count <= 1 || session.Events.Count != frames.Count)
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

        if (!SvgRenderShared.HasTrailingBlankIndicators(session, lastNonBlankIndex + 1))
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

    private static string BuildAnimationCss(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double totalDuration,
        double fadeOut,
        bool loop
    )
    {
        var sb = new LfStringBuilder();

        // Static frame rule (single line).
        sb.AppendLine(".frame { opacity: 0; }");

        // Per-frame animation rules (single line each), grouped before the keyframes.
        for (var i = 0; i < frames.Count; i++)
        {
            sb.AppendLine(
                $$""".frame-{{i}} { animation:k{{i}} {{Format(totalDuration)}}s linear {{(loop ? "infinite" : "forwards")}}; }"""
            );
        }

        // Keyframes (single line each), grouped after all frame rules.
        for (var i = 0; i < frames.Count; i++)
        {
            var isLast = i == frames.Count - 1;
            var start = Percentage(frames[i].Time, totalDuration);
            double end;
            if (isLast)
            {
                // Last frame visible until the final hold period ends, which is totalDuration - fadeOut.
                end = Percentage(totalDuration - fadeOut, totalDuration);
            }
            else
            {
                end = Math.Max(start, Percentage(frames[i + 1].Time, totalDuration));
            }

            var fadeInPoint = Math.Max(0d, start - 0.001d);
            var fadeOutPoint = Math.Min(100d, end + 0.001d);

            string segments;
            if (isLast && fadeOut <= 0d)
            {
                segments = $$"""{{Format(start)}}% { opacity: 1; }""";
                if (start < 100d)
                {
                    segments += " 100% { opacity: 1; }";
                }
            }
            else
            {
                segments = $$"""{{Format(start)}}%, {{Format(end)}}% { opacity: 1; }""";
                if (!isLast || fadeOut > 0d)
                {
                    segments += $$""" {{Format(fadeOutPoint)}}%, 100% { opacity: 0; }""";
                }
            }

            sb.AppendLine(
                $$"""@keyframes k{{i}} { 0%, {{Format(fadeInPoint)}}% { opacity: 0; } {{segments}} }"""
            );
        }

        return sb.ToString();
    }

    private static double Percentage(double value, double total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return Math.Max(0, Math.Min(100, value / total * 100));
    }

    private static bool HaveSameTime(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }

    private static double GetMinimumFrameInterval(double maxFps)
    {
        return maxFps > 0d ? 1d / maxFps : 0.05d;
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
