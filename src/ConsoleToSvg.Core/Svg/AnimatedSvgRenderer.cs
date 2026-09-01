using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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

    public static void Write(TextWriter writer, RecordingSession session, SvgRenderOptions options)
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
    public static string RenderFrames(IReadOnlyList<TerminalFrame> frames, SvgRenderOptions options)
    {
        var builder = new StringBuilder(128 * 1024);
        using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
        WriteFrames(writer, frames, options);
        return builder.ToString();
    }

    public static void WriteFrames(
        TextWriter writer,
        IReadOnlyList<TerminalFrame> frames,
        SvgRenderOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one terminal frame is required.", nameof(frames));
        }

        var theme = SvgRenderShared.ResolveTheme(options);
        var signatureCache = new Dictionary<ScreenBuffer, ulong>();
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
            var filtered = new List<TerminalFrame>(reducedFrames.Count);

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

        TerminalFrame[]? copiedFrames = null;
        ReadOnlySpan<TerminalFrame> animationFrames;
        if (reducedFrames is TerminalFrame[] frameArray)
        {
            animationFrames = frameArray;
        }
        else if (reducedFrames is List<TerminalFrame> frameList)
        {
            animationFrames = CollectionsMarshal.AsSpan(frameList);
        }
        else
        {
            copiedFrames = new TerminalFrame[reducedFrames.Count];
            for (var i = 0; i < reducedFrames.Count; i++)
            {
                copiedFrames[i] = reducedFrames[i];
            }
            animationFrames = copiedFrames;
        }

        var commandHeaderRows = string.IsNullOrEmpty(options.CommandHeader) ? 0 : 1;
        var context = SvgRenderShared.CreateContext(
            animationFrames[0].Buffer,
            options,
            includeScrollback: false,
            commandHeaderRows
        );
        var lastFrameTime = Math.Max(0.05d, animationFrames[^1].Time);
        var finalFrameHold = GetFinalFrameHoldDuration(
            options.VideoSleep,
            options.VideoFadeOut,
            options.VideoFps
        );
        var totalDuration = lastFrameTime + finalFrameHold + options.VideoFadeOut;

        var svgWriter = new SvgWriter(writer);
        var styles = new SvgStyleRegistry();
        var autoMasker =
            options.AutoMask == AutoMaskCategory.None
                ? null
                : new AutoMasker(options.AutoMask, options.AutoMaskHomeDirectory);
        if (context.HeaderRows > 0 && !string.IsNullOrEmpty(options.CommandHeader))
        {
            styles.GetTextClass(theme.Foreground);
        }
        SvgDocumentBuilder.CollectTextStyles(animationFrames, context, styles);
        SvgDocumentBuilder.BeginSvg(
            svgWriter,
            context,
            theme,
            styles,
            additionalCss: null,
            font: options.Font,
            chrome: options.Chrome,
            commandHeader: options.CommandHeader,
            opacity: options.Opacity,
            background: options.Background,
            maskPatterns: options.MaskPatterns,
            autoMasker: autoMasker,
            animateBlink: true
        );

        var frameRowDefinitions = SvgDocumentBuilder.AppendAnimatedRowDefs(
            svgWriter,
            animationFrames,
            context,
            theme,
            styles,
            lengthAdjust: options.LengthAdjust,
            opacity: options.Opacity,
            maskPatterns: options.MaskPatterns,
            autoMasker: autoMasker
        );
        var hasContentTransform = SvgDocumentBuilder.AppendContentTransformGroupOpen(
            svgWriter,
            context
        );
        SvgDocumentBuilder.AppendAnimatedRows(
            svgWriter,
            animationFrames,
            frameRowDefinitions,
            context,
            theme,
            totalDuration,
            options.VideoFadeOut,
            options.Loop
        );
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
        Dictionary<ScreenBuffer, ulong> cache
    )
    {
        if (!cache.TryGetValue(buffer, out var signature))
        {
            signature = buffer.GetVisualSignature();
            cache[buffer] = signature;
        }

        return signature;
    }

    private static IReadOnlyList<TerminalFrame> ReduceFrames(
        IReadOnlyList<TerminalFrame> frames,
        double maxFps,
        Dictionary<ScreenBuffer, ulong> signatureCache
    )
    {
        if (frames.Count <= 2 || maxFps <= 0)
        {
            return frames;
        }

        var minimumInterval = 1d / maxFps;
        var reduced = new List<TerminalFrame>(frames.Count);
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

    private static IReadOnlyList<TerminalFrame> NormalizeTiming(
        IReadOnlyList<TerminalFrame> frames,
        double maxFps,
        VideoTimingMode timingMode
    )
    {
        if (frames.Count == 0 || timingMode == VideoTimingMode.Realtime || maxFps <= 0)
        {
            return frames;
        }

        var interval = 1d / maxFps;
        var normalized = new List<TerminalFrame>(frames.Count);
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
                Math.Round(Math.Max(0d, time) / interval, MidpointRounding.AwayFromZero) * interval;
            if (quantizedTime < lastTime)
            {
                quantizedTime = lastTime;
            }

            lastTime = quantizedTime;
            return quantizedTime;
        };
    }
}
