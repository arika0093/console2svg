using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Recording;

/// <summary>Cross-platform keyboard input event stored in replay files.</summary>
public sealed record InputEvent(
    double Time,
    string Key,
    string[] Modifiers,
    string Type = "keydown"
);

/// <summary>Wrapper type for the JSON replay file: <c>{"Replay":[...]}</c>.</summary>
public sealed class InputReplayData
{
    [JsonPropertyName("Replay")]
    public List<InputEvent> Replay { get; set; } = [];
}

public static class InputReplayFile
{
    // ── VT escape sequence tables ───────────────────────────────────────────

    private static readonly (string Final, string Key)[] s_csiLetterKeys =
    [
        ("A", "ArrowUp"),
        ("B", "ArrowDown"),
        ("C", "ArrowRight"),
        ("D", "ArrowLeft"),
        ("H", "Home"),
        ("F", "End"),
        ("P", "F1"),
        ("Q", "F2"),
        ("R", "F3"),
        ("S", "F4"),
    ];

    private static readonly (string Num, string Key)[] s_csiTildeKeys =
    [
        ("1", "Home"),
        ("2", "Insert"),
        ("3", "Delete"),
        ("4", "End"),
        ("5", "PageUp"),
        ("6", "PageDown"),
        ("11", "F1"),
        ("12", "F2"),
        ("13", "F3"),
        ("14", "F4"),
        ("15", "F5"),
        ("17", "F6"),
        ("18", "F7"),
        ("19", "F8"),
        ("20", "F9"),
        ("21", "F10"),
        ("23", "F11"),
        ("24", "F12"),
    ];

    private static readonly (string Final, string Key)[] s_ss3Keys =
    [
        ("A", "ArrowUp"),
        ("B", "ArrowDown"),
        ("C", "ArrowRight"),
        ("D", "ArrowLeft"),
        ("H", "Home"),
        ("F", "End"),
        ("P", "F1"),
        ("Q", "F2"),
        ("R", "F3"),
        ("S", "F4"),
    ];

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Read all events from a JSON replay file (<c>{"Replay":[...]}</c>).</summary>
    public static async Task<List<InputEvent>> ReadAllAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await File
            .ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return ParseJsonObject(json);
    }

    /// <summary>
    /// Parse a decoded VT input string into a sequence of cross-platform key events.
    /// </summary>
    public static IEnumerable<InputEvent> ParseInputText(string text, double time)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // ESC sequences
            if (c == '\x1b')
            {
                if (i + 1 >= text.Length)
                {
                    yield return new InputEvent(time, "Escape", [], "keydown");
                    i++;
                    continue;
                }
                char next = text[i + 1];
                if (next == '[')
                {
                    var (key, mods, len) = ParseCsiSequence(text, i);
                    if (key is not null)
                        yield return new InputEvent(time, key, mods, "keydown");
                    i += len;
                    continue;
                }
                if (next == 'O')
                {
                    var (key, len) = ParseSs3Sequence(text, i);
                    yield return new InputEvent(time, key, [], "keydown");
                    i += len;
                    continue;
                }
                if (next == '\x1b')
                {
                    // Two consecutive ESCs → first is a lone Escape; second handled next iteration.
                    yield return new InputEvent(time, "Escape", [], "keydown");
                    i++;
                    continue;
                }
                // ESC + char → Alt prefix
                var (altKey, altMods) = CharToKeyAndMods(next);
                yield return new InputEvent(time, altKey, PrependAlt(altMods), "keydown");
                i += 2;
                continue;
            }

            // Common control characters
            if (c == '\x08' || c == '\x7f')
            {
                yield return new InputEvent(time, "Backspace", [], "keydown");
                i++;
                continue;
            }
            if (c == '\x09')
            {
                yield return new InputEvent(time, "Tab", [], "keydown");
                i++;
                continue;
            }
            if (c == '\x0a' || c == '\x0d')
            {
                yield return new InputEvent(time, "Enter", [], "keydown");
                i++; // advance past CR or LF
                // If we just consumed a CR, absorb an immediately following LF so that
                // a Windows-style CR+LF pair produces only one Enter event.
                if (c == '\x0d' && i < text.Length && text[i] == '\x0a')
                    i++;
                continue;
            }

            // Remaining Ctrl+letter range (\x01–\x1a, excluding those handled above)
            if (c >= '\x01' && c <= '\x1a')
            {
                var letter = (char)('a' + (c - 1));
                yield return new InputEvent(time, letter.ToString(), ["ctrl"], "keydown");
                i++;
                continue;
            }

            // Printable (handle surrogate pairs for non-BMP Unicode)
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return new InputEvent(time, text.Substring(i, 2), [], "keydown");
                i += 2;
                continue;
            }

            yield return new InputEvent(time, c.ToString(), [], "keydown");
            i++;
        }
    }

    /// <summary>Convert a structured <see cref="InputEvent"/> back to the VT bytes for the PTY.</summary>
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
                if (shift && alt) return new byte[] { 0x1b, 0x1b, 0x5b, 0x5a };
                if (shift) return new byte[] { 0x1b, 0x5b, 0x5a };
                if (alt) return new byte[] { 0x1b, 0x09 };
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
        int modBits =
            (shift ? 1 : 0) | (alt ? 2 : 0) | (ctrl ? 4 : 0) | (meta ? 8 : 0);
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

    internal static List<InputEvent> ParseJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        var data = JsonSerializer.Deserialize(json, InputReplayJsonContext.Default.InputReplayData);
        return data?.Replay ?? [];
    }

    private static byte[] Enc(string s) => Encoding.UTF8.GetBytes(s);

    private static bool ArrayContains(string[] arr, string val)
    {
        foreach (var s in arr)
            if (s == val)
                return true;
        return false;
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

    private static (string? Key, string[] Mods, int Length) ParseCsiSequence(
        string text,
        int start
    )
    {
        // text[start] == ESC, text[start+1] == '['
        int i = start + 2;
        while (i < text.Length && (text[i] == ';' || (text[i] >= '0' && text[i] <= '9')))
            i++;

        if (i >= text.Length)
            return ("Escape", [], 1); // incomplete — consume only the ESC

        char fin = text[i];
        int len = i - start + 1;
        string param = text.Substring(start + 2, i - (start + 2));

        // Win32-input-mode: \x1b[Vk;Sc;Uc;Kd;Cs;Rc_
        if (fin == '_')
            return ParseWin32InputMode(param, len);

        if (fin == '~')
        {
            var parts = param.Split(';');
            var mods = parts.Length >= 2 ? DecodeVtMods(parts[1]) : (string[])[];
            foreach (var (num, key) in s_csiTildeKeys)
                if (parts[0] == num)
                    return (key, mods, len);
            return (text.Substring(start, len), [], len); // unknown
        }

        if (char.IsLetter(fin))
        {
            var parts = param.Split(';');
            string[] mods = parts.Length >= 2 ? DecodeVtMods(parts[1]) : [];
            var finStr = fin.ToString();
            // Z = Back-Tab (Shift+Tab): carry any decoded modifiers plus an implied shift.
            if (finStr == "Z")
                return ("Tab", PrependShift(mods), len);
            // Focus-in / focus-out events (xterm focus-tracking protocol) — skip silently.
            if ((fin == 'I' || fin == 'O') && param.Length == 0)
                return (null, [], len);
            foreach (var (final, key) in s_csiLetterKeys)
                if (final == finStr)
                    return (key, mods, len);
            return (text.Substring(start, len), [], len); // unknown
        }

        return (text.Substring(start, len), [], len);
    }

    private static (string Key, int Length) ParseSs3Sequence(string text, int start)
    {
        // text[start] == ESC, text[start+1] == 'O'
        if (start + 2 >= text.Length)
            return ("Escape", 1);
        var cStr = text[start + 2].ToString();
        foreach (var (fin, key) in s_ss3Keys)
            if (fin == cStr)
                return (key, 3);
        return (text.Substring(start, 3), 3);
    }

    private static string[] DecodeVtMods(string s)
    {
        if (!int.TryParse(s, out int n))
            return [];
        int bits = n - 1;
        var result = new List<string>(4);
        if ((bits & 1) != 0)
            result.Add("shift");
        if ((bits & 2) != 0)
            result.Add("alt");
        if ((bits & 4) != 0)
            result.Add("ctrl");
        if ((bits & 8) != 0)
            result.Add("meta");
        return [.. result];
    }

    // ── Win32-input-mode helpers ─────────────────────────────────────────────

    /// <summary>
    /// Parse a Win32-input-mode CSI sequence: <c>\x1b[Vk;Sc;Uc;Kd;Cs;Rc_</c>.
    /// Returns null key for key-up or unhandled events (caller skips them).
    /// </summary>
    private static (string? Key, string[] Mods, int Length) ParseWin32InputMode(
        string param,
        int len
    )
    {
        var parts = param.Split(';');
        if (parts.Length < 6)
            return (null, [], len);

        if (
            !int.TryParse(parts[0], out int vk)
            || !int.TryParse(parts[2], out int uc)
            || !int.TryParse(parts[3], out int kd)
            || !int.TryParse(parts[4], out int cs)
        )
            return (null, [], len);

        // Skip key-up events (Kd == 0).
        if (kd == 0)
            return (null, [], len);

        var mods = DecodeWin32ControlState(cs);

        // Named special/function key from Virtual Key code.
        var namedKey = VkToNamedKey(vk);
        if (namedKey is not null)
            return (namedKey, mods, len);

        // Ctrl+letter: Uc is a control character (1–26); derive letter from Vk (A=0x41).
        bool hasCtrl = (cs & 0x0C) != 0;
        if (hasCtrl && vk >= 0x41 && vk <= 0x5A)
        {
            var letter = (char)('a' + (vk - 0x41));
            return (letter.ToString(), mods, len);
        }

        // Printable Unicode character (Uc already reflects shift state).
        if (uc > 0x1F && uc != 0x7F && uc <= 0x10FFFF && (uc < 0xD800 || uc >= 0xE000))
        {
            var keyStr = uc < 0x10000 ? ((char)uc).ToString() : char.ConvertFromUtf32(uc);
            return (keyStr, mods, len);
        }

        // Unhandled (NumLock, CapsLock, etc.) — skip.
        return (null, [], len);
    }

    /// <summary>Decode Win32 control-key state bits into modifier names.</summary>
    private static string[] DecodeWin32ControlState(int cs)
    {
        // Bit 0–1: Right/Left Alt; bit 2–3: Right/Left Ctrl; bit 4: Shift.
        var mods = new List<string>(3);
        if ((cs & 0x0C) != 0)
            mods.Add("ctrl");
        if ((cs & 0x03) != 0)
            mods.Add("alt");
        if ((cs & 0x10) != 0)
            mods.Add("shift");
        return [.. mods];
    }

    /// <summary>Map a Windows Virtual Key code to a named key string, or null if not a special key.</summary>
    private static string? VkToNamedKey(int vk) =>
        vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "ArrowLeft",
            0x26 => "ArrowUp",
            0x27 => "ArrowRight",
            0x28 => "ArrowDown",
            0x2D => "Insert",
            0x2E => "Delete",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => null,
        };

    // ── Writer ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams <see cref="InputEvent"/> objects to a <c>{"Replay":[...]}</c> JSON file
    /// incrementally. Dispose (or <c>await using</c>) to write the closing brackets and flush.
    /// </summary>
    public sealed class InputReplayWriter : IDisposable, IAsyncDisposable
    {
        private readonly TextWriter _writer;
        private bool _hasEvents;

        public InputReplayWriter(TextWriter writer)
        {
            _writer = writer;
            _writer.Write("{\"Replay\":[");
        }

        public void AppendEvent(InputEvent evt)
        {
            if (_hasEvents)
                _writer.Write(",");
            _writer.Write(JsonSerializer.Serialize(evt, InputReplayJsonContext.Default.InputEvent));
            _hasEvents = true;
        }

        public void Dispose()
        {
            _writer.Write("]}");
            _writer.Flush();
            _writer.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.WriteAsync("]}").ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
            if (_writer is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else
                _writer.Dispose();
        }
    }

    // ── Replay stream ────────────────────────────────────────────────────────

    public sealed class ReplayStream : Stream
    {
        private readonly (double Time, byte[] Data)[] _events;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _eventIndex;
        private int _eventOffset;

        public ReplayStream(IList<InputEvent> events)
        {
            _events = new (double, byte[])[events.Count];
            for (var i = 0; i < events.Count; i++)
                _events[i] = (events[i].Time, EventToBytes(events[i]));
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use ReadAsync.");

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            while (_eventIndex < _events.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (time, data) = _events[_eventIndex];

                if (_eventOffset == 0)
                {
                    var delay = time - _stopwatch.Elapsed.TotalSeconds;
                    if (delay > 0)
                    {
                        await Task
                            .Delay(TimeSpan.FromSeconds(delay), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                var available = data.Length - _eventOffset;
                if (available <= 0)
                {
                    _eventIndex++;
                    _eventOffset = 0;
                    continue;
                }

                var toCopy = Math.Min(count, available);
                Array.Copy(data, _eventOffset, buffer, offset, toCopy);
                _eventOffset += toCopy;
                if (_eventOffset >= data.Length)
                {
                    _eventIndex++;
                    _eventOffset = 0;
                }

                return toCopy;
            }

            return 0;
        }
    }
}
