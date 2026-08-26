using System;
using System.Globalization;
using System.IO;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

public static partial class AnimatedSvgRenderer
{
    public static string Render(RecordingSession session, SvgRenderOptions options)
    {
        var builder = new StringBuilder(128 * 1024);
        using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
        Write(writer, session, options);
        return builder.ToString();
    }

    public static void Write(
        TextWriter writer,
        RecordingSession session,
        SvgRenderOptions options
    )
    {
        if (session.Events.Count == 0)
        {
            SvgRenderer.Write(writer, session, options);
            return;
        }

        var theme = SvgRenderShared.ResolveTheme(options);
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var normalizeTime = CreateTimeNormalizer(options.VideoFps, options.VideoTiming);
        var frames = emulator.ReplayFrames(session, options.VideoFps, normalizeTime);
        frames = TrimTrailingAltScreenRestoreFrame(frames, session);

        if (frames.Count == 0)
        {
            SvgRenderer.Write(writer, session, options);
            return;
        }

        WriteFrames(writer, frames, options);
    }

    /// <summary>Renders terminal frames captured from an already-running interactive terminal.</summary>
    public static string RenderFrames(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        SvgRenderOptions options
    )
    {
        var builder = new StringBuilder(128 * 1024);
        using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
        WriteFrames(writer, frames, options);
        return builder.ToString();
    }

    public static void WriteFrames(
        TextWriter writer,
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        SvgRenderOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one terminal frame is required.", nameof(frames));
        }

        var theme = SvgRenderShared.ResolveTheme(options);
        var signatureCache = new System.Collections.Generic.Dictionary<ScreenBuffer, ulong>();
        var reducedFrames =
            frames[0].EventIndex >= 0
                ? frames
                : ReduceFrames(
                    NormalizeTiming(frames, options.VideoFps, options.VideoTiming),
                    options.VideoFps,
                    signatureCache
                );
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
                SvgRenderer.Write(writer, reducedFrames[0].Buffer, options);
                return;
            }

            reducedFrames = filtered;
        }

        // Frame content excludes the cursor so cursor-only differences can share
        // the same <defs> entry. The cursor is emitted with each animated frame.
        var hashToDefsFrameIndices =
            new System.Collections.Generic.Dictionary<
                ulong,
                System.Collections.Generic.List<int>
            >();
        var frameToDefsFrameIndex = new int[reducedFrames.Count];
        var uniqueFrameIndices = new System.Collections.Generic.List<int>(reducedFrames.Count);

        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var hash = reducedFrames[i].Buffer.GetContentSignature();
            var defsIdx = -1;
            if (hashToDefsFrameIndices.TryGetValue(hash, out var candidates))
            {
                for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    var candidate = candidates[candidateIndex];
                    if (
                        reducedFrames[i]
                            .Buffer.HasSameContentState(
                                reducedFrames[uniqueFrameIndices[candidate]].Buffer
                            )
                    )
                    {
                        defsIdx = candidate;
                        break;
                    }
                }
            }

            if (defsIdx < 0)
            {
                candidates ??= [];
                defsIdx = uniqueFrameIndices.Count;
                candidates.Add(defsIdx);
                hashToDefsFrameIndices[hash] = candidates;
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

        var svgWriter = new SvgWriter(writer);
        var styles = new SvgStyleRegistry();
        if (context.HeaderRows > 0 && !string.IsNullOrEmpty(options.CommandHeader))
        {
            styles.GetTextClass(theme.Foreground);
        }
        SvgDocumentBuilder.CollectTextStyles(
            reducedFrames,
            uniqueFrameIndices,
            context,
            styles
        );
        SvgDocumentBuilder.BeginSvg(
            svgWriter,
            context,
            theme,
            styles,
            css,
            font: options.Font,
            chrome: options.Chrome,
            commandHeader: options.CommandHeader,
            opacity: options.Opacity,
            background: options.Background,
            maskPatterns: options.MaskPatterns,
            animateBlink: true
        );

        // Render each unique frame once in <defs>, then reference via <use>.
        SvgDocumentBuilder.AppendFrameDefs(
            svgWriter,
            reducedFrames,
            uniqueFrameIndices,
            context,
            theme,
            styles,
            lengthAdjust: options.LengthAdjust,
            opacity: options.Opacity,
            maskPatterns: options.MaskPatterns
        );
        var hasContentTransform = SvgDocumentBuilder.AppendContentTransformGroupOpen(
            svgWriter,
            context
        );
        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var defsFrameIndex = uniqueFrameIndices[frameToDefsFrameIndex[i]];
            SvgDocumentBuilder.AppendFrameUse(
                svgWriter,
                defsId: $"c2d{defsFrameIndex}",
                frameId: $"c2f{i}",
                frameClass: $"c2 f f{i}",
                reducedFrames[i].Buffer,
                context,
                theme
            );
        }
        if (hasContentTransform)
        {
            svgWriter.Append("</g>\n");
        }

        SvgDocumentBuilder.EndSvg(
            svgWriter,
            options.Opacity,
            options.EmbeddedAsciicast,
            options.EmbeddedLogs,
            options.EmbeddedReplay
        );
    }

    private static ulong GetVisualSignatureCached(
        ScreenBuffer buffer,
        System.Collections.Generic.Dictionary<ScreenBuffer, ulong> cache
    )
    {
        if (!cache.TryGetValue(buffer, out var signature))
        {
            signature = buffer.GetVisualSignature();
            cache[buffer] = signature;
        }

        return signature;
    }

    private static System.Collections.Generic.IReadOnlyList<TerminalFrame> ReduceFrames(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double maxFps,
        System.Collections.Generic.Dictionary<ScreenBuffer, ulong> signatureCache
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
        var lastKeptVisualSignature = GetVisualSignatureCached(frames[0].Buffer, signatureCache);
        TerminalFrame? pendingFrame = null;
        var pendingVisualSignature = 0UL;

        for (var i = 1; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            var visualSignature = GetVisualSignatureCached(frame.Buffer, signatureCache);
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

    private static System.Func<double, double> CreateTimeNormalizer(
        double maxFps,
        VideoTimingMode timingMode
    )
    {
        if (timingMode == VideoTimingMode.Realtime || maxFps <= 0)
        {
            return static time => time;
        }

        var interval = 1d / maxFps;
        var lastTime = 0d;
        return time =>
        {
            var quantizedTime =
                Math.Round(Math.Max(0d, time) / interval, MidpointRounding.AwayFromZero)
                * interval;
            if (quantizedTime < lastTime)
            {
                quantizedTime = lastTime;
            }

            lastTime = quantizedTime;
            return quantizedTime;
        };
    }
}
