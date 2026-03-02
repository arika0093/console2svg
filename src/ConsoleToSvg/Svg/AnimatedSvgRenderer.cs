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
        if (options.Chrome?.ThemeBackgroundOverride is string bgOverride)
        {
            theme = theme.WithBackground(bgOverride);
        }
        var emulator = new TerminalEmulator(session.Header.width, session.Header.height, theme);
        var frames = emulator.ReplayFrames(session);

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
        var context = SvgDocumentBuilder.CreateContext(
            reducedFrames[0].Buffer,
            options.Crop,
            includeScrollback: false,
            options.Chrome,
            options.Padding,
            heightRows: null,
            commandHeaderRows,
            options.FontSize
        );
        var lastFrameTime = Math.Max(0.05d, reducedFrames[reducedFrames.Count - 1].Time);
        var totalDuration = lastFrameTime + options.VideoSleep + options.VideoFadeOut;

        var css = BuildAnimationCss(
            reducedFrames,
            totalDuration,
            options.VideoFadeOut,
            options.Loop
        );

        var sb = new StringBuilder(128 * 1024);
        SvgDocumentBuilder.BeginSvg(
            sb,
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
            sb,
            reducedFrames,
            uniqueFrameIndices,
            context,
            theme,
            opacity: options.Opacity
        );
        for (var i = 0; i < reducedFrames.Count; i++)
        {
            var defsFrameIndex = uniqueFrameIndices[frameToDefsFrameIndex[i]];
            SvgDocumentBuilder.AppendFrameUse(
                sb,
                defsId: $"fd-{defsFrameIndex}",
                frameId: $"frame-{i}",
                frameClass: $"frame frame-{i}"
            );
        }

        SvgDocumentBuilder.EndSvg(sb, options.Opacity);
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
        double totalDuration,
        double fadeOut,
        bool loop
    )
    {
        var sb = new StringBuilder();
        sb.Append(".frame{opacity:0;}");

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
            sb.Append('{');
            sb.Append("0%,");
            sb.Append(Format(fadeInPoint));
            sb.Append("%{opacity:0;}");
            sb.Append(Format(start));
            sb.Append("%,");
            sb.Append(Format(end));
            sb.Append("%{opacity:1;}");
            if (!isLast || fadeOut > 0d)
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
            sb.Append(Format(totalDuration));
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
