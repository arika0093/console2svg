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
    private static (string? Key, string[] Mods, int Length) ParseCsiSequence(string text, int start)
    {
        // text[start] == ESC, text[start+1] == '['
        int i = start + 2;

        // Private parameter prefix: ?, >, <, = (0x3C-0x3F)
        bool hasPrivatePrefix = false;
        if (i < text.Length && text[i] >= '<' && text[i] <= '?')
        {
            hasPrivatePrefix = true;
            i++;
        }

        // Parameter bytes: digits and semicolons
        int paramStart = i;
        while (i < text.Length && (text[i] == ';' || (text[i] >= '0' && text[i] <= '9')))
            i++;
        int paramEnd = i;

        // Intermediate bytes: 0x20-0x2F (space, !, ", #, $, %, &, etc.)
        bool hasIntermediateBytes = false;
        while (i < text.Length && text[i] >= 0x20 && text[i] <= 0x2F)
        {
            hasIntermediateBytes = true;
            i++;
        }

        if (i >= text.Length)
            return ("Escape", [], 1); // incomplete — consume only the ESC

        char fin = text[i];
        int len = i - start + 1;

        // Terminal responses have private prefixes (DA1 ESC[?…c, DA2 ESC[>…c,
        // DECRPM ESC[?…$y, etc.) or intermediate bytes — never user input.
        if (hasPrivatePrefix || hasIntermediateBytes)
            return (null, [], len);

        string param = text.Substring(paramStart, paramEnd - paramStart);

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
    /// Detect consecutive Win32-input-mode VK=0 sequences whose UC values
    /// form a VT escape sequence (e.g. ESC + '[' + 'Z' = Shift+Tab).
    /// Some terminals send VT escape sequences this way instead of using
    /// proper VK codes (VK_TAB, VK_DOWN, etc.).
    /// Returns null if the text at <paramref name="start"/> is not this pattern.
    /// </summary>
    private static (List<InputEvent>? Events, int TotalLength) TryParseWin32VtPassthrough(
        string text,
        int start,
        double time
    )
    {
        // The first sequence must be VK=0 with UC=ESC (0x1B) key-down.
        var first = TryParseWin32VkZeroEvent(text, start);
        if (first == null || first.Value.Uc != 0x1B || first.Value.Kd != 1)
            return (null, 0);

        // Collect UC values from consecutive VK=0 sequences.
        var vtChars = new StringBuilder();
        vtChars.Append('\x1B');
        int totalLen = first.Value.Len;
        int pos = start + first.Value.Len;

        while (pos < text.Length)
        {
            var next = TryParseWin32VkZeroEvent(text, pos);
            if (next == null)
                break;

            totalLen += next.Value.Len;
            pos += next.Value.Len;

            // Only include key-down characters in the reassembled VT string.
            if (next.Value.Kd == 1 && next.Value.Uc >= 0)
                vtChars.Append(
                    next.Value.Uc < 0x10000
                        ? (char)next.Value.Uc
                        : char.ConvertFromUtf32(next.Value.Uc)
                );
        }

        if (vtChars.Length <= 1)
            return (null, 0); // Only ESC with no continuation

        // Re-parse the reassembled VT string through normal input parsing.
        var events = new List<InputEvent>(ParseInputText(vtChars.ToString(), time));
        return events.Count > 0 ? (events, totalLen) : (null, 0);
    }

    /// <summary>
    /// Try to parse a Win32-input-mode CSI sequence at <paramref name="pos"/>
    /// and return its details if VK=0 (character passthrough event).
    /// Returns null if not a Win32-input-mode sequence or VK is not 0.
    /// </summary>
    private static (int Uc, int Kd, int Len)? TryParseWin32VkZeroEvent(string text, int pos)
    {
        if (pos + 2 >= text.Length || text[pos] != '\x1B' || text[pos + 1] != '[')
            return null;

        int i = pos + 2;
        // Win32-input-mode never has a private prefix
        if (i < text.Length && text[i] >= '<' && text[i] <= '?')
            return null;

        int paramStart = i;
        while (i < text.Length && (text[i] == ';' || (text[i] >= '0' && text[i] <= '9')))
            i++;

        if (i >= text.Length || text[i] != '_')
            return null;

        int len = i - pos + 1;
        var parts = text.Substring(paramStart, i - paramStart).Split(';');
        if (parts.Length < 6)
            return null;

        if (
            !int.TryParse(parts[0], out int vk)
            || vk != 0
            || !int.TryParse(parts[2], out int uc)
            || !int.TryParse(parts[3], out int kd)
        )
            return null;

        return (uc, kd, len);
    }

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
    /// Collects <see cref="InputEvent"/> objects and writes the entire
    /// <see cref="InputReplayData"/> JSON to the stream on dispose.
    /// </summary>
    public sealed class InputReplayWriter : IDisposable, IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly InputReplayData _data = new();
        private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

        /// <summary>
        /// Total duration in seconds of the recording session. When set, the value
        /// is written to the replay file and can be used as a timeout when replaying.
        /// </summary>
        public double? TotalDuration { get; set; }

        public InputReplayWriter(Stream stream)
        {
            _stream = stream;
        }

        public void AppendEvent(InputEvent evt)
        {
            _data.Replay.Add(evt);
        }

        /// <summary>
        /// Builds a serialization-ready copy of the data:
        /// sets <see cref="InputReplayData.Version"/> and <see cref="InputReplayData.CreatedAt"/>,
        /// and converts events so the first uses <c>time</c> and the rest use <c>tick</c>.
        /// </summary>
        private InputReplayData BuildSerializableData()
        {
            var result = new InputReplayData
            {
                Version = InputReplayData.CurrentVersion,
                AppVersion = ThisAssembly.AssemblyInformationalVersion,
                CreatedAt = _createdAt,
                TotalDuration = TotalDuration,
                Replay = new List<InputEvent>(_data.Replay.Count),
            };

            double lastTime = 0;
            for (var i = 0; i < _data.Replay.Count; i++)
            {
                var src = _data.Replay[i];
                double absTime = src.Time ?? 0;
                var evt = new InputEvent
                {
                    Key = src.Key,
                    Modifiers = src.Modifiers,
                    Type = src.Type,
                };
                if (i == 0)
                    evt.Time = absTime;
                else
                    evt.Tick = Math.Round(absTime - lastTime, 7);
                lastTime = absTime;
                result.Replay.Add(evt);
            }

            return result;
        }

        public void Dispose()
        {
            JsonSerializer.Serialize(
                _stream,
                BuildSerializableData(),
                s_jsonContext.InputReplayData
            );
            _stream.Flush();
            _stream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await JsonSerializer
                .SerializeAsync(_stream, BuildSerializableData(), s_jsonContext.InputReplayData)
                .ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ── Replay stream ────────────────────────────────────────────────────────

    public sealed class ReplayStream : Stream
    {
        private readonly (double Time, byte[] Data, InputEvent Evt)[] _events;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _eventIndex;
        private int _eventOffset;
        private readonly ILogger? _logger;

        public ReplayStream(IList<InputEvent> events, ILogger? logger = null)
        {
            _logger = logger;
            _events = new (double, byte[], InputEvent)[events.Count];
            for (var i = 0; i < events.Count; i++)
                _events[i] = (events[i].Time ?? 0, EventToBytes(events[i]), events[i]);
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
                var (time, data, evt) = _events[_eventIndex];

                if (_eventOffset == 0)
                {
                    var delay = time - _stopwatch.Elapsed.TotalSeconds;
                    if (delay > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    _logger?.ZLogDebug(
                        $"Replay input: t={time:F3}s Key={evt.Key} Modifiers={string.Join("+", evt.Modifiers)}"
                    );
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
