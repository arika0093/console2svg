using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Terminal;

public sealed class AnsiParserTests
{
    [Test]
    public void DefaultScreenCellHasSafeDefaultProperties()
    {
        var cell = default(ScreenCell);

        cell.Foreground.ShouldBeNull();
        cell.Background.ShouldBeNull();
        cell.Bold.ShouldBeFalse();
        cell.UnderlineColor.ShouldBeNull();
        _ = cell.ToTextStyle();
    }

    [Test]
    public void VisualSignatureMatchesClonedScreenAndChangesWithContent()
    {
        var emulator = new TerminalEmulator(8, 2, Theme.Resolve("dark"));
        emulator.Process("A");
        var clone = emulator.Buffer.Clone();

        clone.GetVisualSignature().ShouldBe(emulator.Buffer.GetVisualSignature());

        emulator.Process("B");
        emulator.Buffer.GetVisualSignature().ShouldNotBe(clone.GetVisualSignature());
    }

    [Test]
    public void ApplySgrColorAndReset()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b[31mA\u001b[0mB");

        var first = emulator.Buffer.GetCell(0, 0);
        var second = emulator.Buffer.GetCell(0, 1);

        first.Text.ShouldBe("A");
        first.Foreground.ShouldBe(theme.AnsiPalette[1]);
        second.Text.ShouldBe("B");
        second.Foreground.ShouldBe(theme.Foreground);
    }

    [Test]
    public void DecSpecialGraphicsRenderBoxDrawingCharacters()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b(0lqkxm\u001b(B");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("\u250C");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("\u2500");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("\u2510");
        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("\u2502");
        emulator.Buffer.GetCell(0, 4).Text.ShouldBe("\u2514");
        emulator.Buffer.GetCell(0, 5).Text.ShouldBe(" ");
    }

    [Test]
    public void DecSpecialGraphicsPersistsAcrossInputChunks()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b(");
        emulator.Process("0lqk");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("\u250C");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("\u2500");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("\u2510");
    }

    [Test]
    public void CsiRepeatRendersRepeatedDecSpecialGraphicsCharacter()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b(0lq\u001b[5bk\u001b(B");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("\u250C");
        for (var col = 1; col <= 6; col++)
        {
            emulator.Buffer.GetCell(0, col).Text.ShouldBe("\u2500");
        }

        emulator.Buffer.GetCell(0, 7).Text.ShouldBe("\u2510");
    }

    [Test]
    public void CsiRepeatRendersRepeatedSpaces()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("A \u001b[3bB");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        for (var col = 1; col <= 4; col++)
        {
            emulator.Buffer.GetCell(0, col).Text.ShouldBe(" ");
        }

        emulator.Buffer.GetCell(0, 5).Text.ShouldBe("B");
    }

    [Test]
    public void LegacyAlternateScreenModesPreserveMainScreen()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("M\u001b[?47hA\u001b[?47lB");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("M");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
    }

    [Test]
    public void DecSaveRestoreRestoresTextStyle()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b[1mA\u001b7\u001b[0m\u001b[2CB\u001b8C");

        emulator.Buffer.GetCell(0, 0).Bold.ShouldBeTrue();
        emulator.Buffer.GetCell(0, 3).Bold.ShouldBeFalse();
        emulator.Buffer.GetCell(0, 1).Bold.ShouldBeTrue();
    }

    [Test]
    public void OriginModeAddressesCursorRelativeToScrollRegion()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 6, theme);

        emulator.Process("\u001b[3;5r\u001b[?6h\u001b[1;1HX");

        emulator.Buffer.GetCell(2, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe(" ");
    }

    [Test]
    public void InsertModeShiftsExistingCharacters()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("ABC\u001b[1D\u001b[4hX\u001b[4l");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("C");
    }

    [Test]
    public void CustomTabStopIsUsed()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b[4G\u001bH\u001b[1G\tX");

        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("X");
    }

    [Test]
    public void SgrDecorationsAndUnderlineColorAreStored()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u001b[5;8;9;53;58;2;1;2;3mX");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Blink.ShouldBeTrue();
        cell.Hidden.ShouldBeTrue();
        cell.Strikethrough.ShouldBeTrue();
        cell.Overline.ShouldBeTrue();
        cell.UnderlineColor.ShouldBe("#010203");
    }

    [Test]
    public void PrivateCsiQuestionMarkMDoesNotApplyUnderline()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // DEC private mode sequence emitted by some terminals; not an SGR underline.
        emulator.Process("\u001b[?4mA");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Underline.ShouldBeFalse();
    }

    [Test]
    public void PrivateCsiGreaterThanMDoesNotApplySgrAttributes()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // xterm modifyOtherKeys sequence; not an SGR style sequence.
        emulator.Process("\u001b[>4;2mA");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Underline.ShouldBeFalse();
        cell.Faint.ShouldBeFalse();
    }

    [Test]
    public void MoveCursorAndOverwrite()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("ABC\u001b[1D!");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("!");
    }

    [Test]
    public void CjkWideCharacterOccupiesTwoColumns()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 中 (U+4E2D) is a CJK wide character
        emulator.Process("\u4e2d");

        var first = emulator.Buffer.GetCell(0, 0);
        var second = emulator.Buffer.GetCell(0, 1);

        first.Text.ShouldBe("\u4e2d");
        first.IsWide.ShouldBeTrue();
        second.IsWideContinuation.ShouldBeTrue();
        emulator.Buffer.CursorCol.ShouldBe(2);
    }

    [Test]
    public void EmojiSurrogatePairStoredAsCluster()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 😀 is U+1F600, encoded as surrogate pair in UTF-16
        emulator.Process("\U0001F600");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("\U0001F600");
        cell.IsWide.ShouldBeTrue();
    }

    [Test]
    public void TrueColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 38;2;255;128;0 = orange RGB foreground
        emulator.Process("\u001b[38;2;255;128;0mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Foreground.ShouldBe("#FF8000");
    }

    [Test]
    public void ScrollbackBufferPreservesScrolledRows()
    {
        var theme = Theme.Resolve("dark");
        // 2-row terminal; write 3 lines to force scrolling
        var emulator = new TerminalEmulator(4, 2, theme);

        emulator.Process("AA\r\nBB\r\nCC");

        // "AA" should have scrolled off into the scrollback
        emulator.Buffer.ScrollbackCount.ShouldBe(1);
        emulator.Buffer.GetScrollbackCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetScrollbackCell(0, 1).Text.ShouldBe("A");

        // Current screen should show BB and CC
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("B");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("C");
    }

    [Test]
    public void ReverseVideoSwapsForegroundAndBackground()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 7 enables reverse video; SGR 27 disables it
        emulator.Process("\u001b[7mA\u001b[27mB");

        var reversed = emulator.Buffer.GetCell(0, 0);
        var normal = emulator.Buffer.GetCell(0, 1);

        reversed.Text.ShouldBe("A");
        reversed.Reversed.ShouldBeTrue();

        normal.Text.ShouldBe("B");
        normal.Reversed.ShouldBeFalse();
    }

    [Test]
    public void ReverseVideoResetByFullReset()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 7 sets reverse, SGR 0 resets all attributes including reverse
        emulator.Process("\u001b[7mA\u001b[0mB");

        emulator.Buffer.GetCell(0, 0).Reversed.ShouldBeTrue();
        emulator.Buffer.GetCell(0, 1).Reversed.ShouldBeFalse();
    }

    [Test]
    public void CombiningMarkAppendedToPreviousCell()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 'e' followed by combining acute accent (U+0301) should be stored as "é" in one cell
        emulator.Process("e\u0301");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("e\u0301");
        // Cursor should still be at column 1 (combining mark doesn't advance)
        emulator.Buffer.CursorCol.ShouldBe(1);
    }

    [Test]
    public void ZeroWidthCharsAreSkipped()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Zero-width space and ZWJ should not advance the cursor or place cells
        emulator.Process("A\u200BB");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.CursorCol.ShouldBe(2);
    }

    [Test]
    public void Ansi256ColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // SGR 38;5;196 = 256-color foreground (red in 6x6x6 cube)
        emulator.Process("\u001b[38;5;196mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        // 256-color index 196 = cube index 180 (r=5,g=0,b=0) = #FF0000
        cell.Foreground.ShouldBe("#FF0000");
    }

    [Test]
    public void ColonDelimitedAnsi256ColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Colon-delimited variant (ISO 8613-6): 38:5:196
        emulator.Process("\u001b[38:5:196mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Foreground.ShouldBe("#FF0000");
    }

    [Test]
    public void ColonDelimitedTrueColorForegroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Colon-delimited variant (ISO 8613-6): 38:2:R:G:B
        emulator.Process("\u001b[38:2:255:128:0mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Foreground.ShouldBe("#FF8000");
    }

    [Test]
    public void ColonDelimitedTrueColorForegroundAppliedWithOmittedColorSpaceId()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // ISO 8613-6 truecolor form with an omitted color-space-id slot: 38:2::R:G:B
        emulator.Process("\u001b[38:2::255:128:0mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Foreground.ShouldBe("#FF8000");
    }

    [Test]
    public void ColonDelimitedTrueColorBackgroundApplied()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Colon-delimited background: 48:2:R:G:B
        emulator.Process("\u001b[48:2:0:128:255mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Background.ShouldBe("#0080FF");
    }

    [Test]
    public void ColonDelimitedTrueColorBackgroundAppliedWithOmittedColorSpaceId()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // ISO 8613-6 truecolor form with an omitted color-space-id slot: 48:2::R:G:B
        emulator.Process("\u001b[48:2::0:128:255mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Background.ShouldBe("#0080FF");
    }

    [Test]
    public void MixedSemicolonAndColonSgrParsedCorrectly()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // Bold (1) via semicolon, then colon-delimited foreground color
        emulator.Process("\u001b[1;38:5:196mA\u001b[0m");

        var cell = emulator.Buffer.GetCell(0, 0);
        cell.Text.ShouldBe("A");
        cell.Bold.ShouldBeTrue();
        cell.Foreground.ShouldBe("#FF0000");
    }

    [Test]
    public void VariationSelectorAppendedToPreviousCell()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        // 🛡 (U+1F6E1) followed by variation selector U+FE0F (emoji presentation)
        // The variation selector should be appended to the emoji cell, not skipped
        emulator.Process("\U0001F6E1\uFE0F");

        var cell = emulator.Buffer.GetCell(0, 0);
        // Cell should contain both the emoji and the variation selector
        cell.Text.ShouldBe("\U0001F6E1\uFE0F");
        cell.IsWide.ShouldBeTrue();
    }

    [Test]
    public void BmpEmojiWithVariationSelectorConsumesTwoColumns()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u2705\uFE0F|");

        var emoji = emulator.Buffer.GetCell(0, 0);
        var continuation = emulator.Buffer.GetCell(0, 1);
        var pipe = emulator.Buffer.GetCell(0, 2);

        emoji.Text.ShouldBe("\u2705\uFE0F");
        emoji.IsWide.ShouldBeTrue();
        continuation.IsWideContinuation.ShouldBeTrue();
        pipe.Text.ShouldBe("|");
    }

    [Test]
    public void BmpEmojiWithoutVariationSelectorConsumesTwoColumns()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(8, 2, theme);

        emulator.Process("\u2705|");

        var emoji = emulator.Buffer.GetCell(0, 0);
        var continuation = emulator.Buffer.GetCell(0, 1);
        var pipe = emulator.Buffer.GetCell(0, 2);

        emoji.Text.ShouldBe("\u2705");
        emoji.IsWide.ShouldBeTrue();
        continuation.IsWideContinuation.ShouldBeTrue();
        pipe.Text.ShouldBe("|");
    }

    [Test]
    public void FullWidthLineDoesNotProduceExtraBlankRow()
    {
        var theme = Theme.Resolve("dark");
        // 4-wide terminal; writing exactly 4 chars then \r\n should stay on row 0 then move to row 1
        var emulator = new TerminalEmulator(4, 3, theme);

        // Fill row 0 completely (4 chars), then CRLF
        emulator.Process("ABCD\r\n");

        // Row 0 should have A B C D
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("B");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("C");
        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("D");

        // Row 1 should be empty (no extra blank row from immediate wrap)
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe(" ");

        // Cursor should be at row 1, col 0
        emulator.Buffer.CursorRow.ShouldBe(1);
        emulator.Buffer.CursorCol.ShouldBe(0);
    }

    [Test]
    public void FullWidthLineThenMoreTextGoesToNextRow()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(4, 3, theme);

        // Fill row 0 completely then write more text without explicit CRLF
        emulator.Process("ABCDXY");

        // Row 0: A B C D
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("D");

        // Row 1: X Y (deferred wrap placed them here)
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(1, 1).Text.ShouldBe("Y");
    }

    [Test]
    public void SplitAnsiSequenceAcrossChunksDoesNotRenderLiteralCodes()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(12, 2, theme);

        emulator.Process("\u001b[");
        emulator.Process("31mA\u001b[");
        emulator.Process("0mB");

        var first = emulator.Buffer.GetCell(0, 0);
        var second = emulator.Buffer.GetCell(0, 1);
        var third = emulator.Buffer.GetCell(0, 2);

        first.Text.ShouldBe("A");
        first.Foreground.ShouldBe(theme.AnsiPalette[1]);
        second.Text.ShouldBe("B");
        second.Foreground.ShouldBe(theme.Foreground);
        third.Text.ShouldBe(" ");
    }

    [Test]
    public void CsiGMovesCursorToAbsoluteColumn()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(12, 2, theme);

        emulator.Process("abc\u001b[10GZ");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("a");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("b");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe("c");
        emulator.Buffer.GetCell(0, 9).Text.ShouldBe("Z");
    }

    [Test]
    public void EscCharsetDesignationDoesNotRenderLiteralB()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(12, 2, theme);

        emulator.Process("\u001b(BX");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void SplitEscCharsetDesignationAcrossChunksDoesNotRenderLiteralB()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(12, 2, theme);

        emulator.Process("\u001b(");
        emulator.Process("B");
        emulator.Process("X");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void DcsXtgettcapSequenceDoesNotRenderLiteralText()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(16, 2, theme);

        emulator.Process("\u001bP+q436f+q6b75\u001b\\X");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void SplitDcsXtgettcapAcrossChunksDoesNotRenderLiteralText()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(16, 2, theme);

        emulator.Process("\u001bP+q436f");
        emulator.Process("+q6b75\u001b");
        emulator.Process("\\X");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void CaretNotationOscSequenceIsSkipped()
    {
        // PTY ECHOCTL converts ESC (0x1b) to the two printable chars '^' + '['.
        // The resulting "^[]10;rgb:e6e6/eded/f3f3^[\" should be treated as invisible.
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(16, 2, theme);

        emulator.Process("^[]10;rgb:e6e6/eded/f3f3^[\\X");

        // OSC content must NOT appear as text; only "X" should be written.
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void CaretNotationOscWithBelTerminatorIsSkipped()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(16, 2, theme);

        emulator.Process("^[]11;rgb:0101/0404/0909\aX");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void CaretNotationOscAcrossChunksIsSkipped()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(16, 2, theme);

        // Sequence split across two chunks
        emulator.Process("^[]10;rgb:e6e6/");
        emulator.Process("eded/f3f3^[\\X");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("X");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
    }

    [Test]
    public void CsiAtInsertsBlankCharacters()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(6, 2, theme);

        emulator.Process("ABCD\u001b[2G\u001b[2@Z");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe("Z");
        emulator.Buffer.GetCell(0, 2).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(0, 3).Text.ShouldBe("B");
        emulator.Buffer.GetCell(0, 4).Text.ShouldBe("C");
        emulator.Buffer.GetCell(0, 5).Text.ShouldBe("D");
    }

    [Test]
    public void CsiKErasesUsingCurrentStyleBackground()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(6, 2, theme);

        // Set blue background, print one char, then erase to end of line.
        emulator.Process("\u001b[44mA\u001b[K");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(0, 1).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(0, 5).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(0, 1).Background.ShouldBe(theme.AnsiPalette[4]);
        emulator.Buffer.GetCell(0, 5).Background.ShouldBe(theme.AnsiPalette[4]);
    }

    [Test]
    public void CsiLInsertsLinesInScrollRegion()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(3, 4, theme);

        emulator.Process("\u001b[1;1H111\u001b[2;1H222\u001b[3;1H333\u001b[4;1H444");
        emulator.Process("\u001b[2;1H\u001b[1L");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("1");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(2, 0).Text.ShouldBe("2");
        emulator.Buffer.GetCell(3, 0).Text.ShouldBe("3");
    }

    [Test]
    public void CsiMDeletesLinesInScrollRegion()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(3, 4, theme);

        emulator.Process("\u001b[1;1H111\u001b[2;1H222\u001b[3;1H333\u001b[4;1H444");
        emulator.Process("\u001b[2;1H\u001b[1M");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("1");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("3");
        emulator.Buffer.GetCell(2, 0).Text.ShouldBe("4");
        emulator.Buffer.GetCell(3, 0).Text.ShouldBe(" ");
    }

    [Test]
    public void DecstbmAndLineFeedScrollOnlyInsideMargins()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(5, 4, theme);

        emulator.Process("\u001b[1;1HAAAAA\u001b[2;1HBBBBB\u001b[3;1HCCCCC\u001b[4;1HDDDDD");
        emulator.Process("\u001b[2;3r\u001b[3;1H\n");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("C");
        emulator.Buffer.GetCell(2, 0).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(3, 0).Text.ShouldBe("D");
    }

    [Test]
    public void EscMReverseIndexScrollsDownInsideMargins()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(3, 4, theme);

        emulator.Process("\u001b[1;1HAAA\u001b[2;1HBBB\u001b[3;1HCCC\u001b[4;1HDDD");
        emulator.Process("\u001b[2;3r\u001b[2;1H\u001bM");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("A");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(2, 0).Text.ShouldBe("B");
        emulator.Buffer.GetCell(3, 0).Text.ShouldBe("D");
    }

    [Test]
    public void CsiSAndCsiTScrollUpDown()
    {
        var theme = Theme.Resolve("dark");
        var emulator = new TerminalEmulator(2, 2, theme);

        emulator.Process("\u001b[1;1HAA\u001b[2;1HBB");
        emulator.Process("\u001b[1S");

        emulator.Buffer.ScrollbackCount.ShouldBe(1);
        emulator.Buffer.GetCell(0, 0).Text.ShouldBe("B");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe(" ");

        emulator.Process("\u001b[1T");

        emulator.Buffer.GetCell(0, 0).Text.ShouldBe(" ");
        emulator.Buffer.GetCell(1, 0).Text.ShouldBe("B");
    }
}
