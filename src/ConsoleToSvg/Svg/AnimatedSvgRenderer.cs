using System;
using System.Globalization;
using System.Text;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

public static class AnimatedSvgRenderer
{
    public static string Render(RecordingSession session, SvgRenderOptions options)
    {
        if (session.Events.Count == 0)
        {
            return SvgRenderer.Render(session, options);
        }

        var theme = Theme.Resolve(options.Theme);
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var frames = emulator.ReplayFrames(session);

        if (frames.Count == 0)
        {
            return SvgRenderer.Render(session, options);
        }

        var reducedFrames = ReduceFrames(frames, options.VideoFps);

        var context = SvgDocumentBuilder.CreateContext(
            reducedFrames[0].Buffer,
            options.Crop,
            includeScrollback: false,
            options.Window,
            options.Padding
        );
        var duration = Math.Max(0.05d, reducedFrames[reducedFrames.Count - 1].Time);

        var css = BuildAnimationCss(reducedFrames, duration, options.Loop);

        var sb = new StringBuilder(128 * 1024);
        SvgDocumentBuilder.BeginSvg(
            sb,
            context,
            theme,
            css,
            font: options.Font,
            windowStyle: options.Window
        );
        for (var i = 0; i < reducedFrames.Count; i++)
        {
            SvgDocumentBuilder.AppendFrameGroup(
                sb,
                reducedFrames[i].Buffer,
                context,
                theme,
                id: $"frame-{i}",
                @class: $"frame frame-{i}"
            );
        }

        SvgDocumentBuilder.EndSvg(sb);
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
        var lastKeptColorSignature = BuildColorSignature(frames[0].Buffer);
        var lastKeptVisualSignature = BuildVisualSignature(frames[0].Buffer);
        TerminalFrame? pendingFrame = null;
        ulong pendingColorSignature = 0;
        ulong pendingVisualSignature = 0;

        for (var i = 1; i < frames.Count - 1; i++)
        {
            var frame = frames[i];
            var colorSignature = BuildColorSignature(frame.Buffer);
            var visualSignature = BuildVisualSignature(frame.Buffer);
            var colorChanged = colorSignature != lastKeptColorSignature;
            var visualChanged = visualSignature != lastKeptVisualSignature;
            if (!visualChanged && frame.Time - lastKeptTime < minimumInterval)
            {
                continue;
            }

            if (colorChanged)
            {
                reduced.Add(frame);
                lastKeptTime = frame.Time;
                lastKeptColorSignature = colorSignature;
                lastKeptVisualSignature = visualSignature;
                pendingFrame = null;
                continue;
            }

            if (frame.Time - lastKeptTime >= minimumInterval)
            {
                reduced.Add(frame);
                lastKeptTime = frame.Time;
                lastKeptColorSignature = colorSignature;
                lastKeptVisualSignature = visualSignature;
                pendingFrame = null;
            }
            else if (visualChanged)
            {
                pendingFrame = frame;
                pendingColorSignature = colorSignature;
                pendingVisualSignature = visualSignature;
            }
        }

        if (pendingFrame is not null && !ReferenceEquals(reduced[reduced.Count - 1], pendingFrame))
        {
            reduced.Add(pendingFrame);
            lastKeptTime = pendingFrame.Time;
            lastKeptColorSignature = pendingColorSignature;
            lastKeptVisualSignature = pendingVisualSignature;
        }

        var last = frames[frames.Count - 1];
        if (!ReferenceEquals(reduced[reduced.Count - 1], last))
        {
            reduced.Add(last);
        }

        return reduced;
    }

    private static ulong BuildColorSignature(ScreenBuffer buffer)
    {
        const ulong fnvOffset = 1469598103934665603UL;

        var signature = fnvOffset;
        for (var row = 0; row < buffer.Height; row++)
        {
            for (var col = 0; col < buffer.Width; col++)
            {
                var cell = buffer.GetCell(row, col);
                if (cell.IsWideContinuation)
                {
                    continue;
                }

                var effectiveFg = cell.Reversed ? cell.Background : cell.Foreground;
                var effectiveBg = cell.Reversed ? cell.Foreground : cell.Background;
                signature = HashString(signature, effectiveFg);
                signature = HashString(signature, effectiveBg);
                signature = HashBool(signature, cell.Bold);
                signature = HashBool(signature, cell.Faint);
            }
        }

        return signature;
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

    private static string BuildAnimationCss(
        System.Collections.Generic.IReadOnlyList<TerminalFrame> frames,
        double duration,
        bool loop
    )
    {
        var sb = new StringBuilder();
        sb.Append(".frame{opacity:0;}");

        for (var i = 0; i < frames.Count; i++)
        {
            var start = Percentage(frames[i].Time, duration);
            var end =
                i == frames.Count - 1
                    ? 100d
                    : Math.Max(start, Percentage(frames[i + 1].Time, duration));
            var fadeInPoint = Math.Max(0d, start - 0.001d);
            var fadeOutPoint = Math.Min(100d, end + 0.001d);

            sb.Append("@keyframes k");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append('{');
            sb.Append("0%,");
            sb.Append(Format(fadeInPoint));
            sb.Append("%{opacity:0;}");
            sb.Append(Format(start));
            sb.Append("%,");
            sb.Append(Format(end));
            sb.Append("%{opacity:1;}");
            if (i < frames.Count - 1)
            {
                sb.Append(Format(fadeOutPoint));
                sb.Append("%,100%{opacity:0;}");
            }

            sb.Append('}');

            sb.Append(".frame-");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append("{animation:k");
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(Format(duration));
            sb.Append("s linear ");
            sb.Append(loop ? "infinite;" : "forwards;");
            sb.Append('}');
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
