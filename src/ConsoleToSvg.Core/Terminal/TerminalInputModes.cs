using System;

namespace ConsoleToSvg.Terminal;

[Flags]
public enum TerminalMouseTrackingModes
{
    None = 0,
    X10 = 1 << 0,
    ButtonEvent = 1 << 1,
    AnyEvent = 1 << 2,
    Click = 1 << 3,
}

[Flags]
public enum TerminalMouseEncodingModes
{
    Default = 0,
    Utf8 = 1 << 0,
    Sgr = 1 << 1,
    Urxvt = 1 << 2,
}

/// <summary>
/// Tracks terminal modes that affect how a controller encodes semantic input.
/// This state is separate from the rendered screen buffer.
/// </summary>
public sealed class TerminalInputModes
{
    public bool ApplicationCursorKeys { get; private set; }
    public bool ApplicationKeypad { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool FocusReporting { get; private set; }
    public TerminalMouseTrackingModes MouseTracking { get; private set; }
    public TerminalMouseEncodingModes MouseEncoding { get; private set; }

    public void SetDecPrivateMode(int mode, bool enabled)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = enabled;
                break;
            case 9:
                MouseTracking = SetFlag(
                    MouseTracking,
                    TerminalMouseTrackingModes.X10,
                    enabled
                );
                break;
            case 1000:
                MouseTracking = SetFlag(MouseTracking, TerminalMouseTrackingModes.Click, enabled);
                break;
            case 1002:
                MouseTracking = SetFlag(
                    MouseTracking,
                    TerminalMouseTrackingModes.ButtonEvent,
                    enabled
                );
                break;
            case 1003:
                MouseTracking = SetFlag(
                    MouseTracking,
                    TerminalMouseTrackingModes.AnyEvent,
                    enabled
                );
                break;
            case 1004:
                FocusReporting = enabled;
                break;
            case 1005:
                MouseEncoding = SetFlag(MouseEncoding, TerminalMouseEncodingModes.Utf8, enabled);
                break;
            case 1006:
                MouseEncoding = SetFlag(MouseEncoding, TerminalMouseEncodingModes.Sgr, enabled);
                break;
            case 1015:
                MouseEncoding = SetFlag(MouseEncoding, TerminalMouseEncodingModes.Urxvt, enabled);
                break;
            case 2004:
                BracketedPaste = enabled;
                break;
        }
    }

    public void SetApplicationKeypad(bool enabled) => ApplicationKeypad = enabled;

    public void Reset()
    {
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPaste = false;
        FocusReporting = false;
        MouseTracking = TerminalMouseTrackingModes.None;
        MouseEncoding = TerminalMouseEncodingModes.Default;
    }

    private static T SetFlag<T>(T value, T flag, bool enabled)
        where T : struct, Enum
    {
        var current = Convert.ToUInt64(value);
        var bit = Convert.ToUInt64(flag);
        return (T)Enum.ToObject(typeof(T), enabled ? current | bit : current & ~bit);
    }
}
