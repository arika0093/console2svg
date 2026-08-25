using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Terminal;

public readonly record struct TextStyle(
    string Foreground,
    string Background,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Reversed = false,
    bool Faint = false,
    bool Hidden = false,
    bool Strikethrough = false,
    bool Overline = false,
    bool Blink = false,
    string? UnderlineColor = null
);

[Flags]
internal enum TextStyleAttributes : ushort
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Reversed = 1 << 3,
    Faint = 1 << 4,
    Hidden = 1 << 5,
    Strikethrough = 1 << 6,
    Overline = 1 << 7,
    Blink = 1 << 8,
}

internal sealed record CellStyle(
    string Foreground,
    string Background,
    string? UnderlineColor,
    TextStyleAttributes Attributes
)
{
    public CellStyle(TextStyle style)
        : this(
            style.Foreground,
            style.Background,
            style.UnderlineColor,
            GetFlags(style)
        )
    {
    }

    private static TextStyleAttributes GetFlags(TextStyle style)
    {
        var flags = TextStyleAttributes.None;
        if (style.Bold)
            flags |= TextStyleAttributes.Bold;
        if (style.Italic)
            flags |= TextStyleAttributes.Italic;
        if (style.Underline)
            flags |= TextStyleAttributes.Underline;
        if (style.Reversed)
            flags |= TextStyleAttributes.Reversed;
        if (style.Faint)
            flags |= TextStyleAttributes.Faint;
        if (style.Hidden)
            flags |= TextStyleAttributes.Hidden;
        if (style.Strikethrough)
            flags |= TextStyleAttributes.Strikethrough;
        if (style.Overline)
            flags |= TextStyleAttributes.Overline;
        if (style.Blink)
            flags |= TextStyleAttributes.Blink;
        return flags;
    }

    public TextStyle ToTextStyle() =>
        new(
            Foreground,
            Background,
            Attributes.HasFlag(TextStyleAttributes.Bold),
            Attributes.HasFlag(TextStyleAttributes.Italic),
            Attributes.HasFlag(TextStyleAttributes.Underline),
            Attributes.HasFlag(TextStyleAttributes.Reversed),
            Attributes.HasFlag(TextStyleAttributes.Faint),
            Attributes.HasFlag(TextStyleAttributes.Hidden),
            Attributes.HasFlag(TextStyleAttributes.Strikethrough),
            Attributes.HasFlag(TextStyleAttributes.Overline),
            Attributes.HasFlag(TextStyleAttributes.Blink),
            UnderlineColor
        );
}

public readonly struct ScreenCell
{
    private const byte Wide = 1 << 0;
    private const byte WideContinuation = 1 << 1;
    private readonly CellStyle _style;
    private readonly byte _flags;

    public ScreenCell(
        string text,
        TextStyle style,
        bool isWide = false,
        bool isWideContinuation = false
    )
        : this(text, new CellStyle(style), isWide, isWideContinuation)
    {
    }

    internal ScreenCell(
        string text,
        CellStyle style,
        bool isWide = false,
        bool isWideContinuation = false
    )
    {
        Text = text;
        _style = style;
        _flags = (byte)((isWide ? Wide : 0) | (isWideContinuation ? WideContinuation : 0));
    }

    public string Text { get; }

    internal CellStyle Style => _style;

    public string Foreground => _style?.Foreground!;

    public string Background => _style?.Background!;

    public bool Bold => HasStyle(TextStyleAttributes.Bold);

    public bool Italic => HasStyle(TextStyleAttributes.Italic);

    public bool Underline => HasStyle(TextStyleAttributes.Underline);

    public bool Reversed => HasStyle(TextStyleAttributes.Reversed);

    public bool Faint => HasStyle(TextStyleAttributes.Faint);
    public bool Hidden => HasStyle(TextStyleAttributes.Hidden);
    public bool Strikethrough => HasStyle(TextStyleAttributes.Strikethrough);
    public bool Overline => HasStyle(TextStyleAttributes.Overline);
    public bool Blink => HasStyle(TextStyleAttributes.Blink);
    public string? UnderlineColor => _style?.UnderlineColor;

    public bool IsWide => (_flags & Wide) != 0;

    public bool IsWideContinuation => (_flags & WideContinuation) != 0;

    public TextStyle ToTextStyle() =>
        new TextStyle(
            Foreground,
            Background,
            Bold,
            Italic,
            Underline,
            Reversed,
            Faint,
            Hidden,
            Strikethrough,
            Overline,
            Blink,
            UnderlineColor
        );

    private bool HasStyle(TextStyleAttributes attribute) =>
        _style is not null && (_style.Attributes & attribute) != 0;
}

public sealed partial class ScreenBuffer
{
    private readonly Theme _theme;
    private ScreenCell[][] _mainCells;
    private ScreenCell[][] _altCells;
    private ScreenCell[][] _cells;
    private bool _isAltScreen;
    private int _savedRow;
    private int _savedCol;
    private int _savedMainRow;
    private int _savedMainCol;
    private int _scrollTop;
    private int _scrollBottom;
    private bool _pendingWrap;
    private bool _originMode;
    private bool _insertMode;
    private readonly SortedSet<int> _tabStops = new();
    private readonly List<ScreenCell[]> _scrollbackRows = new();
    private readonly Dictionary<TextStyle, CellStyle> _styleCache = new();
    private TextStyle _lastTextStyle;
    private CellStyle? _lastCellStyle;
    private ulong[] _rowSignatures;
    private bool[] _rowSignatureDirty;

    public ScreenBuffer(int width, int height, Theme theme)
        : this(width, height, theme, initializeCells: true)
    {
    }

    private ScreenBuffer(int width, int height, Theme theme, bool initializeCells)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _theme = theme;
        DefaultStyle = new TextStyle(theme.Foreground, theme.Background, false, false, false);

        _mainCells = initializeCells ? CreateBlankCells() : null!;
        _altCells = initializeCells ? CreateBlankCells() : null!;
        _cells = _mainCells;
        _rowSignatures = new ulong[Height];
        _rowSignatureDirty = new bool[Height];
        Array.Fill(_rowSignatureDirty, true);
        _scrollTop = 0;
        _scrollBottom = Height - 1;
        CursorRow = 0;
        CursorCol = 0;
        for (var col = 8; col < Width; col += 8)
        {
            _tabStops.Add(col);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public int CursorRow { get; private set; }

    public int CursorCol { get; private set; }

    public TextStyle DefaultStyle { get; }

    public bool OriginMode => _originMode;

    public int ScrollbackCount => _scrollbackRows.Count;

    public int TotalHeight => _scrollbackRows.Count + Height;

    /// <summary>
    /// Returns a stable signature of the visible terminal state. This is useful
    /// when consecutive video samples show the same screen and can reuse an
    /// already-rendered SVG or PNG.
    /// </summary>
    public ulong GetVisualSignature()
    {
        const ulong fnvOffset = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;

        for (var row = 0; row < Height; row++)
        {
            if (!_rowSignatureDirty[row])
            {
                continue;
            }

            var rowSignature = fnvOffset;
            for (var col = 0; col < Width; col++)
            {
                AddCell(ref rowSignature, _cells[row][col]);
            }

            _rowSignatures[row] = rowSignature;
            _rowSignatureDirty[row] = false;
        }

        var signature = fnvOffset;
        AddInt(ref signature, CursorRow);
        AddInt(ref signature, CursorCol);
        for (var row = 0; row < Height; row++)
        {
            AddUlong(ref signature, _rowSignatures[row]);
        }

        return signature;

        static void AddCell(ref ulong hash, ScreenCell cell)
        {
            AddString(ref hash, cell.Text);
            AddString(ref hash, cell.Foreground);
            AddString(ref hash, cell.Background);
            AddBool(ref hash, cell.Bold);
            AddBool(ref hash, cell.Italic);
            AddBool(ref hash, cell.Underline);
            AddBool(ref hash, cell.Reversed);
            AddBool(ref hash, cell.Faint);
            AddBool(ref hash, cell.Hidden);
            AddBool(ref hash, cell.Strikethrough);
            AddBool(ref hash, cell.Overline);
            AddBool(ref hash, cell.Blink);
            AddString(ref hash, cell.UnderlineColor ?? string.Empty);
            AddBool(ref hash, cell.IsWide);
            AddBool(ref hash, cell.IsWideContinuation);
        }

        static void AddString(ref ulong hash, string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= fnvPrime;
            }
            hash ^= 0xFF;
            hash *= fnvPrime;
        }

        static void AddBool(ref ulong hash, bool value)
        {
            hash ^= value ? (byte)1 : (byte)0;
            hash *= fnvPrime;
        }

        static void AddInt(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (byte)value;
                hash *= fnvPrime;
                hash ^= (byte)(value >> 8);
                hash *= fnvPrime;
                hash ^= (byte)(value >> 16);
                hash *= fnvPrime;
                hash ^= (byte)(value >> 24);
                hash *= fnvPrime;
            }
        }

        static void AddUlong(ref ulong hash, ulong value)
        {
            unchecked
            {
                for (var shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= fnvPrime;
                }
            }
        }
    }

    public ScreenCell GetCell(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width)
        {
            return CreateCell(" ", DefaultStyle);
        }

        return _cells[row][col];
    }

    internal bool HasSameVisualState(ScreenBuffer other)
    {
        if (
            Width != other.Width
            || Height != other.Height
            || CursorRow != other.CursorRow
            || CursorCol != other.CursorCol
        )
        {
            return false;
        }

        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                if (!_cells[row][col].Equals(other._cells[row][col]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public ScreenCell GetScrollbackCell(int scrollbackRow, int col)
    {
        if (scrollbackRow < 0 || scrollbackRow >= _scrollbackRows.Count || col < 0 || col >= Width)
        {
            return CreateCell(" ", DefaultStyle);
        }

        return _scrollbackRows[scrollbackRow][col];
    }

    public ScreenCell GetCellFromTop(int row, int col)
    {
        if (row < _scrollbackRows.Count)
        {
            return GetScrollbackCell(row, col);
        }

        return GetCell(row - _scrollbackRows.Count, col);
    }

    public ScreenBuffer Clone()
    {
        var cloned = new ScreenBuffer(Width, Height, _theme, initializeCells: false)
        {
            CursorRow = CursorRow,
            CursorCol = CursorCol,
            _savedRow = _savedRow,
            _savedCol = _savedCol,
            _savedMainRow = _savedMainRow,
            _savedMainCol = _savedMainCol,
            _isAltScreen = _isAltScreen,
            _scrollTop = _scrollTop,
            _scrollBottom = _scrollBottom,
            _pendingWrap = _pendingWrap,
            _originMode = _originMode,
            _insertMode = _insertMode,
            _mainCells = CloneCells(_mainCells),
            _altCells = CloneCells(_altCells),
            _rowSignatures = (ulong[])_rowSignatures.Clone(),
            _rowSignatureDirty = (bool[])_rowSignatureDirty.Clone(),
        };
        cloned._tabStops.Clear();
        cloned._tabStops.UnionWith(_tabStops);

        cloned._cells = cloned._isAltScreen ? cloned._altCells : cloned._mainCells;
        return cloned;
    }

    internal void CopyVisibleStateFrom(ScreenBuffer source)
    {
        if (source.Width != Width || source.Height != Height)
        {
            throw new ArgumentException("Screen buffer dimensions must match.", nameof(source));
        }

        CursorRow = source.CursorRow;
        CursorCol = source.CursorCol;
        _isAltScreen = source._isAltScreen;
        var sourceCells = source._cells;
        var targetCells = _isAltScreen ? _altCells : _mainCells;
        for (var row = 0; row < Height; row++)
        {
            Array.Copy(sourceCells[row], targetCells[row], Width);
        }
        Array.Copy(source._rowSignatures, _rowSignatures, Height);
        Array.Copy(source._rowSignatureDirty, _rowSignatureDirty, Height);
        _cells = targetCells;
    }

    private void SetCell(int row, int col, ScreenCell cell)
    {
        _cells[row][col] = cell;
        _rowSignatureDirty[row] = true;
    }

    internal CellStyle ResolveCellStyle(TextStyle style)
    {
        CellStyle cellStyle;
        if (_lastCellStyle is not null && style == _lastTextStyle)
        {
            cellStyle = _lastCellStyle;
        }
        else if (!_styleCache.TryGetValue(style, out cellStyle!))
        {
            cellStyle = new CellStyle(style);
            if (_styleCache.Count >= 256)
            {
                _styleCache.Clear();
            }
            _styleCache.Add(style, cellStyle);
        }

        _lastTextStyle = style;
        _lastCellStyle = cellStyle;
        return cellStyle;
    }

    private ScreenCell CreateCell(
        string text,
        TextStyle style,
        bool isWide = false,
        bool isWideContinuation = false
    )
    {
        var cellStyle = ResolveCellStyle(style);
        return new ScreenCell(text, cellStyle, isWide, isWideContinuation);
    }

    public void PutChar(char value, TextStyle style)
    {
        PutChar(value, ResolveCellStyle(style));
    }

    internal void PutChar(char value, CellStyle cellStyle)
    {
        if (value == '\n')
        {
            LineFeed();
            return;
        }

        if (value == '\r')
        {
            CarriageReturn();
            return;
        }

        if (value == '\b')
        {
            Backspace();
            return;
        }

        if (value == '\t')
        {
            var nextStop = _tabStops
                .Where(stop => stop > CursorCol)
                .DefaultIfEmpty(Width - 1)
                .First();

            var spaces = Math.Max(1, nextStop - CursorCol);
            for (var i = 0; i < spaces; i++)
            {
                PutPrintable(" ", cellStyle);
            }

            return;
        }

        if (char.IsControl(value))
        {
            return;
        }

        PutPrintable(ToSingleCharString(value), cellStyle);
    }

    public void PutSurrogatePair(string cluster, TextStyle style)
    {
        PutPrintable(cluster, ResolveCellStyle(style));
    }

    internal void PutSurrogatePair(string cluster, CellStyle cellStyle)
    {
        PutPrintable(cluster, cellStyle);
    }

    public void RepeatPreviousCharacter(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var row = CursorRow;
        var col = _pendingWrap ? CursorCol : CursorCol - 1;
        if (col < 0)
        {
            return;
        }

        if (_cells[row][col].IsWideContinuation && col > 0)
        {
            col--;
        }

        var previous = _cells[row][col];
        if (previous.IsWideContinuation)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            PutPrintable(previous.Text, previous.Style);
        }
    }

    public void AppendToPreviousCell(char combining)
    {
        // Find the previous printable cell
        var col = CursorCol - 1;
        var row = CursorRow;
        if (col < 0)
        {
            if (row == 0)
            {
                return;
            }

            row--;
            col = Width - 1;
        }

        // If it's a wide continuation, step back to the actual wide cell
        if (_cells[row][col].IsWideContinuation && col > 0)
        {
            col--;
        }

        var prev = _cells[row][col];
        if (prev.Text == " ")
        {
            return;
        }

        SetCell(row, col, CreateCell(
            prev.Text + ToSingleCharString(combining),
            prev.ToTextStyle(),
            prev.IsWide,
            prev.IsWideContinuation
        ));

        if (combining == '\uFE0F' && !prev.IsWide && !prev.IsWideContinuation)
        {
            TryPromoteCellToWide(row, col);
        }
    }

    private void TryPromoteCellToWide(int row, int col)
    {
        if (col + 1 >= Width)
        {
            return;
        }

        var next = _cells[row][col + 1];
        if (next.Text != " " || next.IsWideContinuation)
        {
            return;
        }

        var cell = _cells[row][col];
        SetCell(row, col, CreateCell(
            cell.Text,
            cell.ToTextStyle(),
            isWide: true,
            isWideContinuation: false
        ));
        SetCell(row, col + 1, CreateCell(
            " ",
            cell.ToTextStyle(),
            isWide: false,
            isWideContinuation: true
        ));

        if (CursorRow == row && CursorCol == col + 1)
        {
            CursorCol++;
            if (CursorCol >= Width)
            {
                _pendingWrap = true;
                CursorCol = Width - 1;
            }
        }
    }

    private void PutPrintable(string text, CellStyle cellStyle)
    {
        // Apply any pending wrap from the previous character filling the last column
        if (_pendingWrap)
        {
            _pendingWrap = false;
            CursorCol = 0;
            Index();
        }

        var isWide = IsWideCharacter(text);
        if (_insertMode)
        {
            InsertBlankCharacters(isWide ? 2 : 1, cellStyle.ToTextStyle());
        }

        if (isWide && CursorCol + 1 >= Width)
        {
            SetCell(CursorRow, CursorCol, CreateCell(" ", DefaultStyle));
            CursorCol = 0;
            Index();
        }

        SetCell(CursorRow, CursorCol, new ScreenCell(text, cellStyle, isWide));
        CursorCol++;

        if (isWide && CursorCol < Width)
        {
            SetCell(
                CursorRow,
                CursorCol,
                new ScreenCell(" ", cellStyle, false, true)
            );
            CursorCol++;
        }

        if (CursorCol >= Width)
        {
            // Deferred wrap: keep cursor at last column, wrap on next printable char
            _pendingWrap = true;
            CursorCol = Width - 1;
        }
    }

    private static readonly string[] AsciiSingleCharStrings = CreateAsciiSingleCharStrings();

    private static string[] CreateAsciiSingleCharStrings()
    {
        var chars = new string[128];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = ((char)i).ToString();
        }

        return chars;
    }

    private static string ToSingleCharString(char value) =>
        value < AsciiSingleCharStrings.Length
            ? AsciiSingleCharStrings[value]
            : value.ToString();

    private static bool IsWideCharacter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int codePoint;
        if (text.Length >= 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]))
        {
            codePoint = char.ConvertToUtf32(text[0], text[1]);
        }
        else if (text.Length == 1)
        {
            codePoint = text[0];
        }
        else
        {
            return false;
        }

        return IsEastAsianWide(codePoint) || IsBmpEmojiWide(codePoint);
    }

    private static bool IsBmpEmojiWide(int cp) =>
        cp
            is 0x2611 // ☑
                or 0x2705 // ✅
                or 0x274C // ❌
                or 0x2753 // ❓
                or 0x2754 // ❔
                or 0x2755 // ❕
                or 0x2757; // ❗

    private static bool IsEastAsianWide(int cp) =>
        cp
            is (>= 0x1100 and <= 0x115F)
                or (>= 0x2E80 and <= 0x2FFD)
                or (>= 0x3000 and <= 0x303F)
                or (>= 0x3040 and <= 0x33FF)
                or (>= 0x3400 and <= 0x4DBF)
                or (>= 0x4E00 and <= 0x9FFF)
                or (>= 0xA000 and <= 0xA48C)
                or (>= 0xA960 and <= 0xA97F)
                or (>= 0xAC00 and <= 0xD7A3)
                or (>= 0xF900 and <= 0xFAFF)
                or (>= 0xFE10 and <= 0xFE1F)
                or (>= 0xFE30 and <= 0xFE6F)
                or (>= 0xFF01 and <= 0xFF60)
                or (>= 0xFFE0 and <= 0xFFE6)
                or (>= 0x1B000 and <= 0x1B0FF)
                or (>= 0x1F004 and <= 0x1F004)
                or (>= 0x1F0CF and <= 0x1F0CF)
                or (>= 0x1F200 and <= 0x1F2FF)
                or (>= 0x1F300 and <= 0x1F64F)
                or (>= 0x1F680 and <= 0x1F6FF)
                or (>= 0x1F900 and <= 0x1F9FF)
                or (>= 0x20000 and <= 0x2FFFD)
                or (>= 0x30000 and <= 0x3FFFD);
}
