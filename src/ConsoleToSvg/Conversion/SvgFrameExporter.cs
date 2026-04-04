using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg.Conversion;

internal static class SvgFrameExporter
{
    public static async Task<int> SaveFramesAsync(
        RecordingSession session,
        SvgRenderOptions baseOptions,
        string directory,
        double fps,
        ILogger logger,
        CancellationToken cancellationToken,
        bool announce = true
    )
    {
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var eventCount = session.Events.Count;
        logger.LogDebug(
            "Saving individual frames to {Directory}. Events={EventCount} Fps={Fps}",
            directory,
            eventCount,
            fps
        );

        if (fps > 0 && eventCount > 0)
        {
            var totalTime = session.Events[eventCount - 1].Time;
            var totalFrames = (int)Math.Floor(totalTime * fps) + 1;
            var interval = 1.0 / fps;

            for (var f = 0; f < totalFrames; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var t = f * interval;
                var eventIndex = -1;
                for (var i = 0; i < eventCount; i++)
                {
                    if (session.Events[i].Time <= t + 1e-9)
                    {
                        eventIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }

                baseOptions.Frame = eventIndex >= 0 ? eventIndex : 0;
                var frameSvg = SvgRenderer.Render(session, baseOptions);
                var framePath = Path.Combine(directory, $"frame-{f:D4}.svg");
                await File.WriteAllTextAsync(framePath, frameSvg, utf8, cancellationToken)
                    .ConfigureAwait(false);
            }

            baseOptions.Frame = null;
            logger.LogDebug("Saved {TotalFrames} frames to {Directory}", totalFrames, directory);
            if (announce)
            {
                await Console.Error.WriteLineAsync($"Saved {totalFrames} frames to {directory}");
            }

            return totalFrames;
        }

        var savedCount = 0;
        string? previousSvg = null;

        for (var i = 0; i < eventCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            baseOptions.Frame = i;
            var frameSvg = SvgRenderer.Render(session, baseOptions);

            if (frameSvg == previousSvg)
            {
                continue;
            }

            previousSvg = frameSvg;
            var framePath = Path.Combine(directory, $"frame-{savedCount:D4}.svg");
            await File.WriteAllTextAsync(framePath, frameSvg, utf8, cancellationToken)
                .ConfigureAwait(false);
            savedCount++;
        }

        baseOptions.Frame = null;
        logger.LogDebug(
            "Saved {SavedCount} unique frames (of {EventCount} events) to {Directory}",
            savedCount,
            eventCount,
            directory
        );
        if (announce)
        {
            await Console.Error.WriteLineAsync($"Saved {savedCount} frames to {directory}");
        }

        return savedCount;
    }
}
