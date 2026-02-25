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

        var context = SvgDocumentBuilder.CreateContext(frames[0].Buffer, options.Crop, includeScrollback: false, options.Window, options.Padding);
        var duration = Math.Max(0.05d, frames[frames.Count - 1].Time);

        var css = BuildAnimationCss(frames, duration);

        var sb = new StringBuilder(128 * 1024);
        SvgDocumentBuilder.BeginSvg(sb, context, theme, css, font: options.Font, windowStyle: options.Window);
        for (var i = 0; i < frames.Count; i++)
        {
            SvgDocumentBuilder.AppendFrameGroup(
                sb,
                frames[i].Buffer,
                context,
                theme,
                id: $"frame-{i}",
                @class: $"frame frame-{i}"
            );
        }

        SvgDocumentBuilder.EndSvg(sb);
        return sb.ToString();
    }

    private static string BuildAnimationCss(System.Collections.Generic.IReadOnlyList<TerminalFrame> frames, double duration)
    {
        var sb = new StringBuilder();
        sb.Append(".frame{opacity:0;}");

        for (var i = 0; i < frames.Count; i++)
        {
            var start = Percentage(frames[i].Time, duration);
            var end = i == frames.Count - 1
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
            sb.Append("s linear forwards;}");
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
