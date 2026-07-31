using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static async Task<int> SaveFramesAsync(
        RecordingSession session,
        SvgRenderOptions baseOptions,
        string directory,
        double fps,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var savedCount = 0;
        foreach (var frameSvg in RenderFrameSvgs(session, baseOptions, fps, cancellationToken))
        {
            await File.WriteAllTextAsync(
                    Path.Combine(directory, $"frame-{savedCount:D4}.svg"), frameSvg, utf8, cancellationToken)
                .ConfigureAwait(false);
            savedCount++;
        }
        logger.ZLogDebug($"Saved {savedCount} frames to {directory}");
        await Console.Error.WriteLineAsync($"Saved {savedCount} frames to {directory}".AsMemory(), CancellationToken.None);
        return savedCount;
    }

    private static IEnumerable<string> RenderFrameSvgs(
        RecordingSession session,
        SvgRenderOptions baseOptions,
        double fps,
        CancellationToken cancellationToken,
        bool includeFallback = false
    )
    {
        var eventCount = session.Events.Count;
        var yielded = false;
        try
        {
            if (fps > 0 && eventCount > 0)
            {
                var totalTime = session.Events[eventCount - 1].Time;
                var totalFrames = (int)Math.Floor(totalTime * fps) + 1;
                var rangeStart = baseOptions.TimeStart ?? 0.0;
                var rangeEnd = baseOptions.TimeEnd ?? totalTime;
                var eventIndex = 0;
                for (var f = 0; f < totalFrames; f++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var t = f / fps;
                    if (t < rangeStart - 1e-9) continue;
                    if (t > rangeEnd + 1e-9) break;
                    while (eventIndex + 1 < eventCount && session.Events[eventIndex + 1].Time <= t + 1e-9) eventIndex++;
                    baseOptions.Frame = eventIndex;
                    yielded = true;
                    yield return SvgRenderer.Render(session, baseOptions);
                }
            }
            else
            {
                string? previousSvg = null;
                for (var i = 0; i < eventCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var time = session.Events[i].Time;
                    if (baseOptions.TimeStart.HasValue && time < baseOptions.TimeStart.Value - 1e-9) continue;
                    if (baseOptions.TimeEnd.HasValue && time > baseOptions.TimeEnd.Value + 1e-9) break;
                    baseOptions.Frame = i;
                    var frameSvg = SvgRenderer.Render(session, baseOptions);
                    if (frameSvg == previousSvg) continue;
                    previousSvg = frameSvg;
                    yielded = true;
                    yield return frameSvg;
                }
            }
            if (!yielded && includeFallback)
            {
                yield return SvgRenderer.Render(session, baseOptions);
            }
        }
        finally { baseOptions.Frame = null; }
    }

    private static string GetDefaultPrompt()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "$";
        }

        try
        {
            return GetEffectiveUserId() == 0 ? "#" : "$";
        }
        catch
        {
            return "$";
        }
    }

    private static uint GetEffectiveUserId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return 1;
        }

        return geteuid();
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static IDisposable ApplyProcessEnvironmentOverrides(AppOptions options, ILogger logger)
    {
        var scope = new EnvironmentVariableScope(logger);

        // Ensure DOTNET_EnableWriteXorExecute=0 is set to prevent potential issues with
        // memory protection on some platforms, especially when dynamic code is involved.
        scope.Set("DOTNET_EnableWriteXorExecute", "0");

        if (!string.IsNullOrWhiteSpace(options.Command) && !options.NoColorEnv)
        {
            logger.ZLogDebug($"Applying color-related environment overrides.");
            // Ensure color-capable settings even on CI runners where TERM is unset/dumb.
            scope.Set("TERM", "xterm-256color");
            scope.Set("COLORTERM", "truecolor");
            scope.Set("FORCE_COLOR", "3");
        }

        return scope;
    }

    private sealed class EnvironmentVariableScope(ILogger logger) : IDisposable
    {
        private readonly Dictionary<string, (bool Exists, string? Value)> _originalValues = new(
            StringComparer.Ordinal
        );
        private readonly List<string> _appliedKeys = [];
        private bool _disposed;

        public void Set(string key, string value) => Apply(key, value);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (var i = _appliedKeys.Count - 1; i >= 0; i--)
            {
                var key = _appliedKeys[i];
                var original = _originalValues[key];
                try
                {
                    Environment.SetEnvironmentVariable(
                        key,
                        original.Exists ? original.Value : null
                    );
                }
                catch (Exception ex)
                {
                    logger.ZLogDebug(ex, $"Failed to restore environment variable: {key}");
                }
            }

            _disposed = true;
            logger.ZLogDebug($"Restored temporary environment variable overrides.");
        }

        private void Apply(string key, string? value)
        {
            if (!_originalValues.ContainsKey(key))
            {
                var original = Environment.GetEnvironmentVariable(key);
                _originalValues[key] = (original is not null, original);
                _appliedKeys.Add(key);
            }

            try
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, $"Failed to update environment variable: {key}");
            }
        }
    }
}
