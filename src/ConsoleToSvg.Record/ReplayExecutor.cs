using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Terminal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZLogger;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Terminal-state observer whose output side is owned by the PTY reader.
/// Replay actions can safely wait on snapshots while output keeps arriving.
/// </summary>
public sealed class ReplayScreenObserver
{
    private readonly object _gate = new();
    private readonly TerminalEmulator _emulator;
    private readonly SemaphoreSlim _outputArrived = new(0);
    private long _generation;

    public ReplayScreenObserver(int width, int height, Theme? theme = null)
    {
        _emulator = new TerminalEmulator(width, height, theme ?? Theme.Resolve("dark"));
    }

    /// <summary>Processes decoded terminal output and wakes pending wait actions.</summary>
    public void ProcessOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        lock (_gate)
        {
            _emulator.Process(output);
            _generation++;
        }
        _outputArrived.Release();
    }

    /// <summary>Gets a normalized screen or scrollback-and-screen snapshot.</summary>
    public string GetText(bool includeScrollback = false)
    {
        lock (_gate)
        {
            return _emulator.Buffer.GetNormalizedText(includeScrollback);
        }
    }

    internal async Task WaitForAsync(
        Func<string, bool> matches,
        bool includeScrollback,
        bool newOutput,
        CancellationToken cancellationToken
    )
    {
        var initialGeneration = Interlocked.Read(ref _generation);
        if (!newOutput && matches(GetText(includeScrollback)))
        {
            return;
        }

        while (true)
        {
            await _outputArrived.WaitAsync(cancellationToken).ConfigureAwait(false);
            var currentGeneration = Interlocked.Read(ref _generation);
            if (newOutput && currentGeneration <= initialGeneration)
            {
                continue;
            }
            if (matches(GetText(includeScrollback)))
            {
                return;
            }
        }
    }
}

/// <summary>Raised when a replay action exceeds its configured timeout.</summary>
public sealed class ReplayTimeoutException(string message) : TimeoutException(message) { }

/// <summary>Executes Replay v2 actions against PTY input and terminal state.</summary>
public static class ReplayExecutor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Executes every replay action in source order.</summary>
    public static async Task ExecuteAsync(
        ReplayDocumentV2 document,
        Stream input,
        ReplayScreenObserver screen,
        CancellationToken cancellationToken,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(screen);
        ReplayDocumentValidation.Validate(document);
        logger ??= NullLogger.Instance;

        using var replayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        if (document.Timeout is not null)
        {
            replayCancellation.CancelAfter(ReplayDuration.Parse(document.Timeout, "timeout"));
        }

        for (var index = 0; index < document.Steps.Count; index++)
        {
            var step = document.Steps[index];
            var timeout = step.Timeout ?? document.Defaults.Timeout;
            using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                replayCancellation.Token
            );
            if (timeout is not null)
            {
                actionCancellation.CancelAfter(
                    ReplayDuration.Parse(timeout, $"steps[{index}].timeout")
                );
            }

            try
            {
                await ExecuteStepAsync(step, input, screen, actionCancellation.Token, logger)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (replayCancellation.IsCancellationRequested)
                {
                    throw new ReplayTimeoutException(
                        $"Replay timed out after {document.Timeout}. Screen: {SanitizeSnapshot(screen.GetText())}"
                    );
                }
                throw new ReplayTimeoutException(
                    $"Replay step {index + 1} timed out after {timeout}. Screen: {SanitizeSnapshot(screen.GetText())}"
                );
            }
        }
    }

    private static async Task ExecuteStepAsync(
        ReplayStep step,
        Stream input,
        ReplayScreenObserver screen,
        CancellationToken cancellationToken,
        ILogger logger
    )
    {
        if (step.WaitFor is ReplayWaitFor waitFor)
        {
            var matcher = waitFor.Regex is string regex
                ? new Regex(regex, RegexOptions.CultureInvariant, RegexTimeout).IsMatch
                : new Func<string, bool>(text =>
                    text.Contains(waitFor.Text!, StringComparison.Ordinal)
                );
            logger.ZLogDebug(
                $"Replay waitFor: {(waitFor.Regex is null ? "text" : "regex")} Scope={waitFor.Scope} NewOutput={waitFor.NewOutput}"
            );
            await screen
                .WaitForAsync(
                    matcher,
                    string.Equals(waitFor.Scope, "scrollback", StringComparison.OrdinalIgnoreCase),
                    waitFor.NewOutput,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        if (step.Sleep is not null)
        {
            await Task.Delay(ReplayDuration.Parse(step.Sleep, "sleep"), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (step.Input is not null)
        {
            var interval = step.Interval is null
                ? TimeSpan.Zero
                : ReplayDuration.Parse(step.Interval, "interval");
            var enumerator = step.Input.EnumerateRunes().GetEnumerator();
            var hasRune = enumerator.MoveNext();
            while (hasRune)
            {
                var bytes = Encoding.UTF8.GetBytes(enumerator.Current.ToString());
                await input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await input.FlushAsync(cancellationToken).ConfigureAwait(false);
                hasRune = enumerator.MoveNext();
                if (hasRune && interval > TimeSpan.Zero)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
            }
            return;
        }

        var bytesToWrite = step.Raw is not null
            ? Encoding.UTF8.GetBytes(step.Raw)
            : InputReplayFile.EventToBytes(CreateKeyEvent(step));
        await input.WriteAsync(bytesToWrite, cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static InputEvent CreateKeyEvent(ReplayStep step)
    {
        var key = step.Key!;
        var sourceModifiers = step.Modifiers;
        string[] modifiers = sourceModifiers is null ? [] : new string[sourceModifiers.Count];
        if (sourceModifiers is not null)
        {
            for (var index = 0; index < sourceModifiers.Count; index++)
            {
                modifiers[index] = sourceModifiers[index].ToLowerInvariant();
            }
        }
        var plusIndex = key.LastIndexOf('+');
        if (plusIndex > 0 && plusIndex < key.Length - 1)
        {
            var parts = key.Split('+', StringSplitOptions.TrimEntries);
            key = parts[^1];
            Array.Resize(ref modifiers, modifiers.Length + parts.Length - 1);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                modifiers[modifiers.Length - (parts.Length - 1) + index] = parts[index]
                    .ToLowerInvariant();
            }
        }

        return new InputEvent
        {
            Key = key,
            Modifiers = modifiers,
            Type = "keydown",
        };
    }

    private static string SanitizeSnapshot(string text)
    {
        const int maximumLength = 512;
        var result = new StringBuilder(Math.Min(text.Length, maximumLength));
        foreach (var character in text)
        {
            if (result.Length >= maximumLength)
            {
                result.Append('…');
                break;
            }
            result.Append(
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t'
                    ? '�'
                    : character
            );
        }
        return result
            .ToString()
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
