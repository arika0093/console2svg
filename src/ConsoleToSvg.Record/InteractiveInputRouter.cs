using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ConsoleToSvg.Recording;

/// <summary>Classifies interactive capture keys while preserving all other VT input.</summary>
public enum InteractiveInputAction
{
    None,
    Screenshot,
    ToggleRecording,
    TogglePause,
    Exit,
}

public sealed class InteractiveInputRouter
{
    private readonly byte[] _screenshotKey;
    private readonly byte[] _recordingKey;
    private readonly byte[] _pauseKey;
    private readonly List<byte> _pending;
    private bool _discardingSgrMouseReport;
    private byte? _pendingWindowsExtendedKey;

    public InteractiveInputRouter(
        ReadOnlySpan<byte> screenshotKey,
        ReadOnlySpan<byte> recordingKey,
        ReadOnlySpan<byte> pauseKey
    )
    {
        _screenshotKey = screenshotKey.ToArray();
        _recordingKey = recordingKey.ToArray();
        _pauseKey = pauseKey.ToArray();
        _pending = new List<byte>(
            Math.Max(_screenshotKey.Length, Math.Max(_recordingKey.Length, _pauseKey.Length))
        );
    }

    public bool HasStandaloneEscape => _pending.Count == 1 && _pending[0] == 0x1b;

    /// <summary>
    /// Routes one byte. Non-capture sequences are appended to <paramref name="forwarded"/>
    /// unchanged and as a single sequence.
    /// </summary>
    public InteractiveInputAction Process(
        byte value,
        List<byte> forwarded,
        bool captureControlsEnabled = true
    )
    {
        if (TryProcessWindowsExtendedKey(value, forwarded))
        {
            return InteractiveInputAction.None;
        }

        if (value == 0x04)
        {
            // Let Unix shells receive EOT so Bash can close normally. Windows
            // cmd.exe does not treat it as EOF, so the caller also receives the
            // explicit Exit action and can close the recording session there.
            forwarded.Add(value);
            return InteractiveInputAction.Exit;
        }

        // live-server does not reserve F9/F10/F12, but Ctrl+D must remain an
        // application-level exit request on Windows, where cmd.exe ignores EOT.
        if (!captureControlsEnabled)
        {
            forwarded.Add(value);
            return InteractiveInputAction.None;
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
        if (
            IsPrefix(_pending, _screenshotKey)
            || IsPrefix(_pending, _recordingKey)
            || IsPrefix(_pending, _pauseKey)
        )
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

            if (_pending.Count == _pauseKey.Length && IsPrefix(_pending, _pauseKey))
            {
                _pending.Clear();
                return InteractiveInputAction.TogglePause;
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

    private bool TryProcessWindowsExtendedKey(byte value, List<byte> forwarded)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        if (_pendingWindowsExtendedKey is { } prefix)
        {
            _pendingWindowsExtendedKey = null;
            var sequence = value switch
            {
                0x48 => "\u001b[A", // Up
                0x50 => "\u001b[B", // Down
                0x4D => "\u001b[C", // Right
                0x4B => "\u001b[D", // Left
                0x47 => "\u001b[H", // Home
                0x4F => "\u001b[F", // End
                0x49 => "\u001b[5~", // Page Up
                0x51 => "\u001b[6~", // Page Down
                0x52 => "\u001b[2~", // Insert
                0x53 => "\u001b[3~", // Delete
                _ => null,
            };
            if (sequence is not null)
            {
                forwarded.AddRange(System.Text.Encoding.ASCII.GetBytes(sequence));
                return true;
            }

            forwarded.Add(prefix);
            return false;
        }

        if (value is 0x00 or 0xE0)
        {
            _pendingWindowsExtendedKey = value;
            return true;
        }

        return false;
    }
}
