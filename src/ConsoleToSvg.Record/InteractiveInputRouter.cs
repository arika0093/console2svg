using System;
using System.Collections.Generic;

namespace ConsoleToSvg.Recording;

/// <summary>Classifies interactive capture keys while preserving all other VT input.</summary>
public enum InteractiveInputAction
{
    None,
    Screenshot,
    ToggleRecording,
    Exit,
}

public sealed class InteractiveInputRouter
{
    private readonly byte[] _screenshotKey;
    private readonly byte[] _recordingKey;
    private readonly List<byte> _pending;
    private bool _discardingSgrMouseReport;

    public InteractiveInputRouter(ReadOnlySpan<byte> screenshotKey, ReadOnlySpan<byte> recordingKey)
    {
        _screenshotKey = screenshotKey.ToArray();
        _recordingKey = recordingKey.ToArray();
        _pending = new List<byte>(Math.Max(_screenshotKey.Length, _recordingKey.Length));
    }

    public bool HasStandaloneEscape => _pending.Count == 1 && _pending[0] == 0x1b;

    /// <summary>
    /// Routes one byte. Non-capture sequences are appended to <paramref name="forwarded"/>
    /// unchanged and as a single sequence.
    /// </summary>
    public InteractiveInputAction Process(byte value, List<byte> forwarded)
    {
        if (value == 0x04)
        {
            return InteractiveInputAction.Exit;
        }

        if (_discardingSgrMouseReport)
        {
            if (value is (byte)'M' or (byte)'m')
            {
                _discardingSgrMouseReport = false;
            }

            return InteractiveInputAction.None;
        }

        _pending.Add(value);
        if (IsPrefix(_pending, _screenshotKey) || IsPrefix(_pending, _recordingKey))
        {
            if (_pending.Count == _screenshotKey.Length && IsPrefix(_pending, _screenshotKey))
            {
                _pending.Clear();
                return InteractiveInputAction.Screenshot;
            }

            if (_pending.Count == _recordingKey.Length && IsPrefix(_pending, _recordingKey))
            {
                _pending.Clear();
                return InteractiveInputAction.ToggleRecording;
            }

            return InteractiveInputAction.None;
        }

        if (IsSgrMouseReportPrefix(_pending))
        {
            _pending.Clear();
            _discardingSgrMouseReport = true;
            return InteractiveInputAction.None;
        }

        ForwardPending(forwarded);
        return InteractiveInputAction.None;
    }

    public void ForwardPending(List<byte> forwarded)
    {
        forwarded.AddRange(_pending);
        _pending.Clear();
    }

    private static bool IsPrefix(List<byte> value, ReadOnlySpan<byte> expected)
    {
        if (value.Count > expected.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Count; i++)
        {
            if (value[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSgrMouseReportPrefix(List<byte> value) =>
        value.Count == 3 && value[0] == 0x1b && value[1] == (byte)'[' && value[2] == (byte)'<';
}
