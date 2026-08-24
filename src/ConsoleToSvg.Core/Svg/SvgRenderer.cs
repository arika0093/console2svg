using System;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;
using ConsoleToSvg.Utils;

namespace ConsoleToSvg.Svg;

public static class SvgRenderer
{
    public static string Render(RecordingSession session, SvgRenderOptions options)
    {
        var theme = SvgRenderShared.ResolveTheme(options);
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);

        // If --time is specified, convert to a frame index using binary search.
        int? effectiveFrame = options.Frame;
        if (!effectiveFrame.HasValue && options.Time.HasValue)
        {
            effectiveFrame = TimeToFrameIndex(session, options.Time.Value);
        }

        ScreenBuffer renderBuffer;
        if (effectiveFrame.HasValue)
        {
            // Explicit --frame/--time path: replay up to the target frame once.
            if (effectiveFrame.Value >= 0)
            {
                emulator.Replay(session, effectiveFrame.Value);
            }

            renderBuffer = emulator.Buffer;
        }
        else
        {
            // Default path: replay once with no per-event buffer clones. Track the
            // last non-blank frame index as we go (IsBlankFrame short-circuits on
            // the first non-blank cell, so this is cheap for populated frames).
            var lastNonBlankIndex = -1;
            for (var i = 0; i < session.Events.Count; i++)
            {
                emulator.Process(session.Events[i].Data);
                if (!SvgRenderShared.IsBlankFrame(emulator.Buffer))
                {
                    lastNonBlankIndex = i;
                }
            }

            var targetFrame = ResolveDefaultTargetFrame(session, lastNonBlankIndex);
            if (targetFrame >= session.Events.Count - 1)
            {
                // The final buffer is already the target (the common case).
                renderBuffer = emulator.Buffer;
            }
            else
            {
                // Rare: trailing blank frames to trim; replay to the target frame.
                var target = new TerminalEmulator(session.Header.width, session.Header.height, theme);
                target.Replay(session, targetFrame);
                renderBuffer = target.Buffer;
            }
        }

        return Render(renderBuffer, options, includeScrollback: effectiveFrame == null);
    }

    /// <summary>Renders an already-emulated terminal screen.</summary>
    public static string Render(
        ScreenBuffer buffer,
        SvgRenderOptions options,
        bool includeScrollback = false
    )
    {
        var theme = SvgRenderShared.ResolveTheme(options);
        var commandHeaderRows = string.IsNullOrEmpty(options.CommandHeader) ? 0 : 1;
        var context = SvgRenderShared.CreateContext(
            buffer,
            options,
            includeScrollback,
            commandHeaderRows
        );
        var sb = new LfStringBuilder(32 * 1024);
        SvgDocumentBuilder.BeginSvg(
            sb.Inner,
            context,
            theme,
            additionalCss: null,
            font: options.Font,
            chrome: options.Chrome,
            commandHeader: options.CommandHeader,
            opacity: options.Opacity,
            background: options.Background,
            maskPatterns: options.MaskPatterns
        );
        SvgDocumentBuilder.AppendFrameGroup(
            sb.Inner,
            buffer,
            context,
            theme,
            id: null,
            @class: null,
            includeScrollback,
            lengthAdjust: options.LengthAdjust,
            maskPatterns: options.MaskPatterns
        );
        SvgDocumentBuilder.EndSvg(sb.Inner, options.Opacity);
        return sb.ToString();
    }

    private static int ResolveDefaultTargetFrame(
        RecordingSession session,
        int lastNonBlankIndex
    )
    {
        var lastIndex = session.Events.Count - 1;
        if (lastIndex <= 0)
        {
            return lastIndex;
        }

        if (lastNonBlankIndex < 0 || lastNonBlankIndex == lastIndex)
        {
            return lastIndex;
        }

        if (!SvgRenderShared.HasTrailingBlankIndicators(session, lastNonBlankIndex + 1))
        {
            return lastIndex;
        }

        return lastNonBlankIndex;
    }

    // Blank/trailing-frame detection moved to SvgRenderShared.

    /// <summary>
    /// Finds the index of the event whose timestamp is closest to the requested time.
    /// Uses binary search for efficiency.
    /// </summary>
    public static int TimeToFrameIndex(RecordingSession session, double timeSeconds)
    {
        var events = session.Events;
        if (events.Count == 0)
        {
            return -1;
        }

        if (timeSeconds <= events[0].Time)
        {
            return 0;
        }

        var lastIndex = events.Count - 1;
        if (timeSeconds >= events[lastIndex].Time)
        {
            return lastIndex;
        }

        // Binary search for the closest event time.
        var lo = 0;
        var hi = lastIndex;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var midTime = events[mid].Time;
            if (Math.Abs(midTime - timeSeconds) < 1e-9)
            {
                return mid;
            }

            if (midTime < timeSeconds)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // lo points to the first event after the requested time; hi points to the last event before it.
        // Pick whichever is closer.
        if (lo >= events.Count)
        {
            return hi;
        }

        if (hi < 0)
        {
            return lo;
        }

        return hi;
    }
}
