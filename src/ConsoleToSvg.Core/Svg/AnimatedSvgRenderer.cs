using System;
using System.Globalization;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using ConsoleToSvg.Utils;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
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

        if (frames.Count == 0)
        {
            return SvgRenderer.Render(session, options);
        }

        return RenderFrames(frames, options);
    }

    /// <summary>Renders terminal frames captured from an already-running interactive terminal.</summary>
    public static string RenderFrames(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        SvgRenderOptions options
    )
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one terminal frame is required.", nameof(frames));
        }

        var theme = SvgRenderShared.ResolveTheme(options);
        frames = NormalizeTiming(frames, options.VideoFps, options.VideoTiming);
        var reducedFrames = ReduceFrames(frames, options.VideoFps);
        reducedFrames = SpreadCollapsedFrameTimes(reducedFrames, options.VideoFps);

        // Filter frames by time range when --time is specified in video mode.
        if (options.TimeStart.HasValue || options.TimeEnd.HasValue)
        {
            var rangeStart = options.TimeStart ?? double.MinValue;
            var rangeEnd = options.TimeEnd ?? double.MaxValue;
            var filtered = new System.Collections.Generic.List<TerminalFrame>(reducedFrames.Count);

            // Keep track of the last frame before rangeStart so the clip
            // starts with the terminal state visible at rangeStart.
            TerminalFrame? lastBeforeRange = null;

            foreach (var f in reducedFrames)
            {
                if (f.Time < rangeStart - 1e-9)
                {
                    lastBeforeRange = f;
                }
                else if (f.Time >= rangeStart - 1e-9 && f.Time <= rangeEnd + 1e-9)
                {
                    filtered.Add(f);
                }
                // Frames after rangeEnd are skipped (times are monotonically increasing)
            }

            // Prepend the last frame before rangeStart so the clip shows the
            // correct terminal state at the start of the time window.
            if (lastBeforeRange is not null)
            {
                filtered.Insert(
                    0,
                    options.TimeStart.HasValue
                        ? new TerminalFrame(rangeStart, lastBeforeRange.Buffer)
                        : lastBeforeRange
                );
            }

            // Rebase timestamps so the clip starts at t=0 instead of the
            // original absolute time (e.g., --time 5-6 → clip from 0 to 1s).
            if (filtered.Count > 0)
            {
                var baseTime = options.TimeStart ?? filtered[0].Time;
                for (var i = 0; i < filtered.Count; i++)
                {
                    filtered[i] = new TerminalFrame(
                        filtered[i].Time - baseTime,
                        filtered[i].Buffer
                    );
                }
            }

            if (filtered.Count == 0)
            {
                return SvgRenderer.Render(reducedFrames[0].Buffer, options);
            }

            reducedFrames = filtered;
        }

        // Build a dedup map: visual-hash → index of the first reduced frame with that hash.
        // Frames that are visually identical (e.g. in a looping animation) will share a single
        // <defs> entry and be referenced via <use>, dramatically reducing file size.
        var hashToDefsFrameIndex = new System.Collections.Generic.Dictionary<ulong, int>();
        var frameToDefsFrameIndex = new int[reducedFrames.Count];
        var uniqueFrameIndices = new System.Collections.Generic.List<int>(reducedFrames.Count);

        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var hash = reducedFrames[i].Buffer.GetVisualSignature();
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
        var finalFrameHold = GetFinalFrameHoldDuration(
            options.VideoSleep,
            options.VideoFadeOut,
            options.VideoFps
        );
        var totalDuration = lastFrameTime + finalFrameHold + options.VideoFadeOut;

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
            background: options.Background,
            maskPatterns: options.MaskPatterns
        );

        // Render each unique frame once in <defs>, then reference via <use>.
        SvgDocumentBuilder.AppendFrameDefs(
            sb.Inner,
            reducedFrames,
            uniqueFrameIndices,
            context,
            theme,
            lengthAdjust: options.LengthAdjust,
            opacity: options.Opacity,
            maskPatterns: options.MaskPatterns
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
        var lastKeptVisualSignature = frames[0].Buffer.GetVisualSignature();
        TerminalFrame? pendingFrame = null;
        var pendingVisualSignature = 0UL;

        for (var i = 1; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            var visualSignature = frame.Buffer.GetVisualSignature();
            var visualChanged = visualSignature != lastKeptVisualSignature;
            var elapsed = frame.Time - lastKeptTime;

            if (elapsed < minimumInterval)
            {
                if (visualChanged)
                {
                    // Keep the latest visual change in the throttled window so that
                    // multi-chunk updates (e.g., line-by-line drawing) settle to the
                    // most complete frame available within the interval.
                    pendingFrame = frame;
                    pendingVisualSignature = visualSignature;
                }
                continue;
            }

            if (pendingFrame is not null)
            {
                reduced.Add(pendingFrame);
                lastKeptTime = pendingFrame.Time;
                lastKeptVisualSignature = pendingVisualSignature;
                pendingFrame = null;

                visualChanged = visualSignature != lastKeptVisualSignature;
                elapsed = frame.Time - lastKeptTime;
                if (elapsed < minimumInterval)
                {
                    if (visualChanged)
                    {
                        pendingFrame = frame;
                        pendingVisualSignature = visualSignature;
                    }
                    continue;
                }

                if (!visualChanged)
                {
                    continue;
                }
            }

            reduced.Add(frame);
            lastKeptTime = frame.Time;
            lastKeptVisualSignature = visualSignature;
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
}
