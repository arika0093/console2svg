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
        var targetFrame =
            options.Frame
            ?? ResolveDefaultTargetFrame(
                session,
                new TerminalEmulator(session.Header.width, session.Header.height, theme)
            );
        if (targetFrame >= 0)
        {
            emulator.Replay(session, targetFrame);
        }

        var commandHeaderRows = string.IsNullOrEmpty(options.CommandHeader) ? 0 : 1;
        var includeScrollback = options.Frame == null;
        var context = SvgRenderShared.CreateContext(
            emulator.Buffer,
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
            background: options.Background
        );
        SvgDocumentBuilder.AppendFrameGroup(
            sb.Inner,
            emulator.Buffer,
            context,
            theme,
            id: null,
            @class: null,
            includeScrollback,
            lengthAdjust: options.LengthAdjust
        );
        SvgDocumentBuilder.EndSvg(sb.Inner, options.Opacity);
        return sb.ToString();
    }

    private static int ResolveDefaultTargetFrame(
        RecordingSession session,
        TerminalEmulator probeEmulator
    )
    {
        var lastIndex = session.Events.Count - 1;
        if (lastIndex <= 0)
        {
            return lastIndex;
        }

        var frames = probeEmulator.ReplayFrames(session);
        if (frames.Count != session.Events.Count)
        {
            return lastIndex;
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
}
