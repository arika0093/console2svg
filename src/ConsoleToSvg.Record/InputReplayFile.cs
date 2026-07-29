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
    // Use default json options tuned for human readability
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly InputReplaySerializerContext s_jsonContext = new(s_jsonOptions);

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
        var data = await ReadDataAsync(path, cancellationToken).ConfigureAwait(false);
        return data.Replay;
    }

    /// <summary>
    /// Read the full replay data (including metadata such as <see cref="InputReplayData.TotalDuration"/>)
    /// from a JSON replay file.
    /// </summary>
    public static async Task<InputReplayData> ReadDataAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return ParseJsonData(json);
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
                    yield return new InputEvent
                    {
                        Time = time,
                        Key = "Escape",
                        Modifiers = [],
                        Type = "keydown",
                    };
                    i++;
                    continue;
                }
                char next = text[i + 1];
                if (next == ']' || next == 'P' || next == '_' || next == '^' || next == 'X')
                {
                    // OSC / DCS / APC / PM / SOS control strings are terminal protocol
                    // traffic, not user key input. Skip them entirely.
                    // OSC may end with BEL or ST (ESC \); the others use ST.
                    bool allowBelTerminator = next == ']';
                    int controlLen = TryGetEscControlStringLength(text, i, allowBelTerminator);
                    if (controlLen > 0)
                    {
                        i += controlLen;
                        continue;
                    }
                }
                if (next == '[')
                {
                    // Some terminals (e.g. copilot CLI) send VT escape sequences
                    // through Win32-input-mode as individual VK=0 character events.
                    // Detect and reassemble them before regular CSI parsing.
                    var (vtEvents, vtLen) = TryParseWin32VtPassthrough(text, i, time);
                    if (vtEvents is not null)
                    {
                        foreach (var evt in vtEvents)
                            yield return evt;
                        i += vtLen;
                        continue;
                    }

                    var (key, mods, len) = ParseCsiSequence(text, i);
                    if (key is not null)
                        yield return new InputEvent
                        {
                            Time = time,
                            Key = key,
                            Modifiers = mods,
                            Type = "keydown",
                        };
                    i += len;
                    continue;
                }
                if (next == 'O')
                {
                    var (key, len) = ParseSs3Sequence(text, i);
                    yield return new InputEvent
                    {
                        Time = time,
                        Key = key,
                        Modifiers = [],
                        Type = "keydown",
                    };
                    i += len;
                    continue;
                }
                if (next == '\x1b')
                {
                    // Two consecutive ESCs → first is a lone Escape; second handled next iteration.
                    yield return new InputEvent
                    {
                        Time = time,
                        Key = "Escape",
                        Modifiers = [],
                        Type = "keydown",
                    };
                    i++;
                    continue;
                }
                // ESC + char → Alt prefix
                var (altKey, altMods) = CharToKeyAndMods(next);
                yield return new InputEvent
                {
                    Time = time,
                    Key = altKey,
                    Modifiers = PrependAlt(altMods),
                    Type = "keydown",
                };
                i += 2;
                continue;
            }

            // Common control characters
            if (c == '\x08' || c == '\x7f')
            {
                yield return new InputEvent
                {
                    Time = time,
                    Key = "Backspace",
                    Modifiers = [],
                    Type = "keydown",
                };
                i++;
                continue;
            }
            if (c == '\x09')
            {
                yield return new InputEvent
                {
                    Time = time,
                    Key = "Tab",
                    Modifiers = [],
                    Type = "keydown",
                };
                i++;
                continue;
            }
            if (c == '\x0a' || c == '\x0d')
            {
                yield return new InputEvent
                {
                    Time = time,
                    Key = "Enter",
                    Modifiers = [],
                    Type = "keydown",
                };
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
                yield return new InputEvent
                {
                    Time = time,
                    Key = letter.ToString(),
                    Modifiers = ["ctrl"],
                    Type = "keydown",
                };
                i++;
                continue;
            }

            // Printable (handle surrogate pairs for non-BMP Unicode)
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return new InputEvent
                {
                    Time = time,
                    Key = text.Substring(i, 2),
                    Modifiers = [],
                    Type = "keydown",
                };
                i += 2;
                continue;
            }

            yield return new InputEvent
            {
                Time = time,
                Key = c.ToString(),
                Modifiers = [],
                Type = "keydown",
            };
            i++;
        }
    }

    /// <summary>
    /// Like <see cref="ParseInputText"/> but returns any trailing incomplete ESC sequence
    /// as <c>Remainder</c> instead of misinterpreting it. The caller should
    /// prepend <c>Remainder</c> to the next chunk of input.
    /// </summary>
    public static (List<InputEvent> Events, string Remainder) ParseInputTextPartial(
        string text,
        double time
    )
    {
        // If the tail contains an incomplete ESC sequence, split from that
        // sequence start so protocol bytes are not misinterpreted as Alt+chars.
        int incompleteStart = FindFirstIncompleteEscSequenceStart(text);
        if (incompleteStart >= 0)
        {
            var head = incompleteStart > 0 ? text.Substring(0, incompleteStart) : "";
            var remainder = text.Substring(incompleteStart);
            var events = new List<InputEvent>(ParseInputText(head, time));
            return (events, remainder);
        }

        return (new List<InputEvent>(ParseInputText(text, time)), "");
    }

    /// <summary>
    /// Returns the index of the first ESC that starts an incomplete terminal
    /// sequence in <paramref name="text"/>, or -1 when all ESC sequences are complete.
    /// </summary>
    private static int FindFirstIncompleteEscSequenceStart(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '\x1b')
            {
                i++;
                continue;
            }

            if (i + 1 >= text.Length)
                return i;

            char next = text[i + 1];

            // CSI
            if (next == '[')
            {
                int csiLen = TryGetCsiSequenceLength(text, i);
                if (csiLen == 0)
                    return i;
                i += csiLen;
                continue;
            }

            // SS3
            if (next == 'O')
            {
                if (i + 2 >= text.Length)
                    return i;
                i += 3;
                continue;
            }

            // OSC / DCS / APC / PM / SOS control strings
            if (next == ']' || next == 'P' || next == '_' || next == '^' || next == 'X')
            {
                bool allowBelTerminator = next == ']';
                int controlLen = TryGetEscControlStringLength(text, i, allowBelTerminator);
                if (controlLen == 0)
                    return i;
                i += controlLen;
                continue;
            }

            // ESC ESC: first ESC is complete; second ESC is handled in next iteration.
            if (next == '\x1b')
            {
                i += 1;
                continue;
            }

            // ESC + char (Alt+key) complete in 2 chars.
            i += 2;
        }

        return -1;
    }

    private static int TryGetCsiSequenceLength(string text, int start)
    {
        if (start + 1 >= text.Length || text[start] != '\x1b' || text[start + 1] != '[')
            return 0;

        int i = start + 2;

        // Private parameter prefix: ?, >, <, =
        if (i < text.Length && text[i] >= '<' && text[i] <= '?')
            i++;

        // Parameter bytes: digits and semicolons
        while (i < text.Length && (text[i] == ';' || (text[i] >= '0' && text[i] <= '9')))
            i++;

        // Intermediate bytes: 0x20-0x2F
        while (i < text.Length && text[i] >= 0x20 && text[i] <= 0x2F)
            i++;

        if (i >= text.Length)
            return 0;

        return i - start + 1;
    }

    /// <summary>
    /// Try to parse an ESC-prefixed control string sequence and return its total
    /// length in chars (including ESC and terminator) when complete.
    /// Returns 0 when incomplete or not a recognized control-string introducer.
    /// </summary>
    private static int TryGetEscControlStringLength(string text, int start, bool allowBelTerminator)
    {
        if (start + 1 >= text.Length || text[start] != '\x1b')
            return 0;

        char kind = text[start + 1];
        if (kind != ']' && kind != 'P' && kind != '_' && kind != '^' && kind != 'X')
            return 0;

        int i = start + 2;
        while (i < text.Length)
        {
            char c = text[i];

            // ST terminator: ESC \
            if (c == '\x1b')
            {
                if (i + 1 < text.Length && text[i + 1] == '\\')
                    return i - start + 2;

                // Any other ESC is part of payload/next sequence; continue scanning.
                i++;
                continue;
            }

            // OSC also allows BEL terminator.
            if (allowBelTerminator && c == '\x07')
                return i - start + 1;

            i++;
        }

        return 0;
    }
}
