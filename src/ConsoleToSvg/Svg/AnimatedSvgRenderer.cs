using System;
using System.Globalization;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using ConsoleToSvg.Utils;

namespace ConsoleToSvg.Svg;

public static class AnimatedSvgRenderer
{
    public static string Render(RecordingSession session, SvgRenderOptions options)
    {
        if (session.Events.Count == 0)
        {
            return SvgRenderer.Render(session, options);
        }

        var theme = SvgRenderShared.ResolveTheme(options);
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var frames = emulator.ReplayFrames(session);
        frames = TrimTrailingAltScreenRestoreFrame(frames, session);
        frames = NormalizeTiming(frames, options.VideoFps, options.VideoTiming);

        if (frames.Count == 0)
        {
            return SvgRenderer.Render(session, options);
        }

        var reducedFrames = ReduceFrames(frames, options.VideoFps);

        // Build a dedup map: visual-hash → index of the first reduced frame with that hash.
        // Frames that are visually identical (e.g. in a looping animation) will share a single
        // <defs> entry and be referenced via <use>, dramatically reducing file size.
        var hashToDefsFrameIndex = new System.Collections.Generic.Dictionary<ulong, int>();
        var frameToDefsFrameIndex = new int[reducedFrames.Count];
        var uniqueFrameIndices = new System.Collections.Generic.List<int>(reducedFrames.Count);

        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var hash = BuildVisualSignature(reducedFrames[i].Buffer);
            if (!hashToDefsFrameIndex.TryGetValue(hash, out var defsIdx))
            {
                defsIdx = uniqueFrameIndices.Count;
                hashToDefsFrameIndex[hash] = defsIdx;
                uniqueFrameIndices.Add(i);
            }

            frameToDefsFrameIndex[i] = defsIdx;
        }

        var commandHeaderRows = string.IsNullOrEmpty(options.CommandHeader) ? 0 : 1;
        var context = SvgRenderShared.CreateContext(
            reducedFrames[0].Buffer,
            options,
            includeScrollback: false,
            commandHeaderRows
        );
        var lastFrameTime = Math.Max(0.05d, reducedFrames[reducedFrames.Count - 1].Time);
        var totalDuration = lastFrameTime + options.VideoSleep + options.VideoFadeOut;

        var css = BuildAnimationCss(
            reducedFrames,
            totalDuration,
            options.VideoFadeOut,
            options.Loop
        );

        var sb = new LfStringBuilder(128 * 1024);
        SvgDocumentBuilder.BeginSvg(
            sb.Inner,
            context,
            theme,
            css,
            font: options.Font,
            chrome: options.Chrome,
            commandHeader: options.CommandHeader,
            opacity: options.Opacity,
            background: options.Background
        );

        // Render each unique frame once in <defs>, then reference via <use>.
        SvgDocumentBuilder.AppendFrameDefs(
            sb.Inner,
            reducedFrames,
            uniqueFrameIndices,
            context,
            theme,
            lengthAdjust: options.LengthAdjust,
            opacity: options.Opacity
        );
        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var defsFrameIndex = uniqueFrameIndices[frameToDefsFrameIndex[i]];
            SvgDocumentBuilder.AppendFrameUse(
                sb.Inner,
                defsId: $"fd-{defsFrameIndex}",
                frameId: $"frame-{i}",
                frameClass: $"frame frame-{i}"
            );
        }

        SvgDocumentBuilder.EndSvg(sb.Inner, options.Opacity);
        return sb.ToString();
    }

    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> ReduceFrames(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double maxFps
    )
    {
        if (frames.Count <= 2 || maxFps <= 0)
        {
            return frames;
        }

        var minimumInterval = 1d / maxFps;
        var reduced = new System.Collections.Generic.List<TerminalFrame>(frames.Count);
        reduced.Add(frames[0]);
        var lastKeptTime = frames[0].Time;
        var lastKeptVisualSignature = BuildVisualSignature(frames[0].Buffer);
        TerminalFrame? pendingFrame = null;

        for (var i = 1; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            var visualSignature = BuildVisualSignature(frame.Buffer);
            var visualChanged = visualSignature != lastKeptVisualSignature;
            if (!visualChanged && frame.Time - lastKeptTime < minimumInterval)
            {
                continue;
            }

            if (frame.Time - lastKeptTime >= minimumInterval)
            {
                reduced.Add(frame);
                lastKeptTime = frame.Time;
                lastKeptVisualSignature = visualSignature;
                pendingFrame = null;
            }
            else if (visualChanged)
            {
                pendingFrame = frame;
            }
        }

        if (pendingFrame is not null && !ReferenceEquals(reduced[reduced.Count - 1], pendingFrame))
        {
            reduced.Add(pendingFrame);
        }

        var last = frames[frames.Count - 1];
        if (!ReferenceEquals(reduced[reduced.Count - 1], last))
        {
            reduced.Add(last);
        }

        return reduced;
    }

    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> NormalizeTiming(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double maxFps,
        VideoTimingMode timingMode
    )
    {
        if (frames.Count == 0 || timingMode == VideoTimingMode.Realtime || maxFps <= 0)
        {
            return frames;
        }

        var interval = 1d / maxFps;
        var normalized = new System.Collections.Generic.List<TerminalFrame>(frames.Count);
        var lastTime = 0d;

        for (var i = 0; i < frames.Count; i++)
        {
            var rawTime = Math.Max(0d, frames[i].Time);
            var quantizedTime =
                Math.Round(rawTime / interval, MidpointRounding.AwayFromZero) * interval;
            if (i > 0 && quantizedTime < lastTime)
            {
                quantizedTime = lastTime;
            }

            normalized.Add(new TerminalFrame(quantizedTime, frames[i].Buffer));
            lastTime = quantizedTime;
        }

        return normalized;
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
        sb.AppendLine(".frame {");
        sb.AppendLine("  opacity: 0;");
        sb.AppendLine("}");

        for (var i = 0; i < frames.Count; i++)
        {
            var isLast = i == frames.Count - 1;
            var start = Percentage(frames[i].Time, totalDuration);
            double end;
            if (isLast)
            {
                // Last frame visible until (lastFrameTime + sleep) which is totalDuration - fadeOut
                end = Percentage(totalDuration - fadeOut, totalDuration);
            }
            else
            {
                end = Math.Max(start, Percentage(frames[i + 1].Time, totalDuration));
            }

            var fadeInPoint = Math.Max(0d, start - 0.001d);
            var fadeOutPoint = Math.Min(100d, end + 0.001d);

            sb.Append("@keyframes k");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(" {");
            sb.Append("  0%, ");
            sb.Append(Format(fadeInPoint));
            sb.AppendLine("% {");
            sb.AppendLine("    opacity: 0;");
            sb.AppendLine("  }");

            if (isLast && fadeOut <= 0d)
            {
                sb.Append("  ");
                sb.Append(Format(start));
                sb.AppendLine("% {");
                sb.AppendLine("    opacity: 1;");
                sb.AppendLine("  }");
                if (start < 100d)
                {
                    sb.AppendLine("  100% {");
                    sb.AppendLine("    opacity: 1;");
                    sb.AppendLine("  }");
                }
            }
            else
            {
                sb.Append("  ");
                sb.Append(Format(start));
                sb.Append("%, ");
                sb.Append(Format(end));
                sb.AppendLine("% {");
                sb.AppendLine("    opacity: 1;");
                sb.AppendLine("  }");
                if (!isLast || fadeOut > 0d)
                {
                    sb.Append("  ");
                    sb.Append(Format(fadeOutPoint));
                    sb.AppendLine("%, 100% {");
                    sb.AppendLine("    opacity: 0;");
                    sb.AppendLine("  }");
                }
            }

            sb.AppendLine("}");

            sb.Append(".frame-");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append(" { animation:k");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(Format(totalDuration));
            sb.Append("s linear ");
            sb.Append(loop ? "infinite;" : "forwards;");
            sb.AppendLine(" }");
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

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
