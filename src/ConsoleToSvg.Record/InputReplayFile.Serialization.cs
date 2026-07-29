using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg.Recording;

public static partial class InputReplayFile
{
    public static byte[] EventToBytes(InputEvent evt)
    {
        if (evt.Type == "raw")
            return Encoding.UTF8.GetBytes(evt.Key);

        bool shift = ArrayContains(evt.Modifiers, "shift");
        bool alt = ArrayContains(evt.Modifiers, "alt");
        bool ctrl = ArrayContains(evt.Modifiers, "ctrl");
        bool meta = ArrayContains(evt.Modifiers, "meta");

        // Named special keys
        switch (evt.Key)
        {
            case "Enter":
                return alt ? new byte[] { 0x1b, 0x0d } : new byte[] { 0x0d };
            case "Tab":
                if (shift && alt)
                    return new byte[] { 0x1b, 0x1b, 0x5b, 0x5a };
                if (shift)
                    return new byte[] { 0x1b, 0x5b, 0x5a };
                if (alt)
                    return new byte[] { 0x1b, 0x09 };
                return new byte[] { 0x09 };
            case "Escape":
                return alt ? new byte[] { 0x1b, 0x1b } : new byte[] { 0x1b };
            case "Backspace":
                return alt ? new byte[] { 0x1b, 0x7f } : new byte[] { 0x7f };
            case "Space":
                return alt ? new byte[] { 0x1b, 0x20 } : new byte[] { 0x20 };
        }

        // Ctrl+letter (a–z)
        if (ctrl && evt.Key.Length == 1)
        {
            char ch = char.ToLowerInvariant(evt.Key[0]);
            if (ch >= 'a' && ch <= 'z')
            {
                byte b = (byte)(ch - 'a' + 1);
                return alt ? new byte[] { 0x1b, b } : new byte[] { b };
            }
        }

        // Alt+single printable char
        if (alt && !ctrl && evt.Key.Length >= 1)
        {
            var encoded = Encoding.UTF8.GetBytes(evt.Key);
            var altResult = new byte[1 + encoded.Length];
            altResult[0] = 0x1b;
            encoded.CopyTo(altResult, 1);
            return altResult;
        }

        // Cursor / function keys with optional VT modifier parameter
        int modBits = (shift ? 1 : 0) | (alt ? 2 : 0) | (ctrl ? 4 : 0) | (meta ? 8 : 0);
        string modParam = modBits > 0 ? $";{modBits + 1}" : "";

        return evt.Key switch
        {
            "ArrowUp" => Enc(modBits > 0 ? $"\x1b[1{modParam}A" : "\x1b[A"),
            "ArrowDown" => Enc(modBits > 0 ? $"\x1b[1{modParam}B" : "\x1b[B"),
            "ArrowRight" => Enc(modBits > 0 ? $"\x1b[1{modParam}C" : "\x1b[C"),
            "ArrowLeft" => Enc(modBits > 0 ? $"\x1b[1{modParam}D" : "\x1b[D"),
            "Home" => Enc(modBits > 0 ? $"\x1b[1{modParam}H" : "\x1b[H"),
            "End" => Enc(modBits > 0 ? $"\x1b[1{modParam}F" : "\x1b[F"),
            "Insert" => Enc(modBits > 0 ? $"\x1b[2{modParam}~" : "\x1b[2~"),
            "Delete" => Enc(modBits > 0 ? $"\x1b[3{modParam}~" : "\x1b[3~"),
            "PageUp" => Enc(modBits > 0 ? $"\x1b[5{modParam}~" : "\x1b[5~"),
            "PageDown" => Enc(modBits > 0 ? $"\x1b[6{modParam}~" : "\x1b[6~"),
            "F1" => Enc(modBits > 0 ? $"\x1b[1{modParam}P" : "\x1bOP"),
            "F2" => Enc(modBits > 0 ? $"\x1b[1{modParam}Q" : "\x1bOQ"),
            "F3" => Enc(modBits > 0 ? $"\x1b[1{modParam}R" : "\x1bOR"),
            "F4" => Enc(modBits > 0 ? $"\x1b[1{modParam}S" : "\x1bOS"),
            "F5" => Enc(modBits > 0 ? $"\x1b[15{modParam}~" : "\x1b[15~"),
            "F6" => Enc(modBits > 0 ? $"\x1b[17{modParam}~" : "\x1b[17~"),
            "F7" => Enc(modBits > 0 ? $"\x1b[18{modParam}~" : "\x1b[18~"),
            "F8" => Enc(modBits > 0 ? $"\x1b[19{modParam}~" : "\x1b[19~"),
            "F9" => Enc(modBits > 0 ? $"\x1b[20{modParam}~" : "\x1b[20~"),
            "F10" => Enc(modBits > 0 ? $"\x1b[21{modParam}~" : "\x1b[21~"),
            "F11" => Enc(modBits > 0 ? $"\x1b[23{modParam}~" : "\x1b[23~"),
            "F12" => Enc(modBits > 0 ? $"\x1b[24{modParam}~" : "\x1b[24~"),
            // Unknown key name: fall back to raw UTF-8
            _ => Encoding.UTF8.GetBytes(evt.Key),
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    internal static List<InputEvent> ParseJsonObject(string json) => ParseJsonData(json).Replay;

    internal static InputReplayData ParseJsonData(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InputReplayData();
        }
        var data =
            JsonSerializer.Deserialize(json, s_jsonContext.InputReplayData)
            ?? new InputReplayData();
        ResolveAbsoluteTimes(data.Replay);
        return data;
    }

    /// <summary>
    /// Resolves each event's absolute time from either its explicit <c>time</c> or
    /// a cumulative <c>tick</c> (delta from the previous event).
    /// <c>time</c> always takes priority when both fields are present.
    /// After resolution all events carry an absolute <see cref="InputEvent.Time"/> and
    /// <see cref="InputEvent.Tick"/> is cleared.
    /// </summary>
    private static void ResolveAbsoluteTimes(List<InputEvent> events)
    {
        double lastTime = 0;
        foreach (var evt in events)
        {
            if (evt.Time.HasValue)
            {
                // Explicit absolute time always wins.
                lastTime = evt.Time.Value;
            }
            else if (evt.Tick.HasValue)
            {
                evt.Time = lastTime + evt.Tick.Value;
                lastTime = evt.Time.Value;
            }
            else
            {
                // Neither specified: keep at same time as previous event.
                evt.Time = lastTime;
            }
            evt.Tick = null;
        }
    }

    private static byte[] Enc(string s) => Encoding.UTF8.GetBytes(s);

    private static bool ArrayContains(string[] arr, string val)
    {
        return Array.Exists(arr, s => s == val);
    }

    private static string[] PrependAlt(string[] mods)
    {
        if (ArrayContains(mods, "alt"))
            return mods;
        var result = new string[mods.Length + 1];
        result[0] = "alt";
        mods.CopyTo(result, 1);
        return result;
    }

    private static string[] PrependShift(string[] mods)
    {
        if (ArrayContains(mods, "shift"))
            return mods;
        var result = new string[mods.Length + 1];
        result[0] = "shift";
        mods.CopyTo(result, 1);
        return result;
    }

    /// <summary>Convert a single char to a key name + modifiers (used for Alt+char sequences).</summary>
    private static (string Key, string[] Modifiers) CharToKeyAndMods(char c)
    {
        if (c == '\x08' || c == '\x7f')
            return ("Backspace", []);
        if (c == '\x09')
            return ("Tab", []);
        if (c == '\x0a' || c == '\x0d')
            return ("Enter", []);
        if (c >= '\x01' && c <= '\x1a')
            return (((char)('a' + c - 1)).ToString(), ["ctrl"]);
        return (c.ToString(), []);
    }
}
