using System;
using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ConsoleToSvg.QuickLeaks;

/// <summary>Describes one secret-like match found in an input string.</summary>
/// <param name="RuleId">The Betterleaks or ConsoleToSvg rule identifier.</param>
/// <param name="Start">The zero-based UTF-16 start offset of the match.</param>
/// <param name="End">The exclusive zero-based UTF-16 end offset of the match.</param>
public readonly record struct QuickLeaksFinding(string RuleId, int Start, int End);

/// <summary>Selects the generated pattern set used by <see cref="QuickLeaks"/>.</summary>
public enum QuickLeaksScanMode
{
    Normal,
    Early,
}

/// <summary>
/// Provides fast, source-generated secret detection without external runtime dependencies.
/// </summary>
public static partial class QuickLeaks
{
    /// <summary>
    /// Scans UTF-16 text without converting it to a string and writes findings to a
    /// caller-owned buffer. The scanner itself does not allocate on the managed heap.
    /// </summary>
    public static int Scan(
        ReadOnlySpan<char> text,
        IBufferWriter<QuickLeaksFinding> destination,
        QuickLeaksScanMode mode = QuickLeaksScanMode.Normal
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        var sink = new FindingSink(text, destination);
        ScanGeneratedRules(text, mode, ref sink);
        return sink.Count;
    }

    internal static QuickLeaksFinding[] ScanRegexFallbackForTesting(
        Regex regex,
        string text,
        ushort ruleIndex = 0
    )
    {
        var buffer = new ArrayBufferWriter<QuickLeaksFinding>();
        var sink = new FindingSink(text, buffer);
        FindRuleMatches(regex, text, ruleIndex, ref sink);
        return buffer.WrittenSpan.ToArray();
    }

    internal static QuickLeaksFinding NarrowFindingForTesting(
        string text,
        ushort ruleIndex,
        int start,
        int end
    ) => NarrowFinding(text, ruleIndex, start, end, GetPostProcessor(ruleIndex));

    private static void VerifyPrefixToken(
        ReadOnlySpan<char> text,
        int anchorStart,
        ushort ruleIndex,
        ReadOnlySpan<char> prefix,
        StringComparison prefixComparison,
        ulong classLowMask,
        ulong classHighMask,
        int normalMinimum,
        int earlyMinimum,
        int maximum,
        bool leadingBoundary,
        bool trailingBoundary,
        QuickLeaksScanMode mode,
        ref FindingSink sink
    )
    {
        if (
            (uint)anchorStart > (uint)text.Length
            || prefix.Length > text.Length - anchorStart
            || (leadingBoundary && !IsRegexWordBoundary(text, anchorStart))
            || !text[anchorStart..].StartsWith(prefix, prefixComparison)
        )
        {
            return;
        }

        var valueStart = anchorStart + prefix.Length;
        var available = text.Length - valueStart;
        var maximumLength = maximum < 0 ? available : Math.Min(available, maximum);
        var valueLength = 0;
        while (
            valueLength < maximumLength
            && IsInAsciiCharacterClass(text[valueStart + valueLength], classLowMask, classHighMask)
        )
        {
            valueLength++;
        }

        var minimum = mode == QuickLeaksScanMode.Early ? earlyMinimum : normalMinimum;
        var matchEnd = valueStart + valueLength;
        if (valueLength < minimum || (trailingBoundary && !IsRegexWordBoundary(text, matchEnd)))
        {
            return;
        }

        sink.Add(ruleIndex, anchorStart, matchEnd);
    }

    private static bool IsInAsciiCharacterClass(char value, ulong lowMask, ulong highMask)
    {
        if (value >= 128)
        {
            return false;
        }
        var mask = value < 64 ? lowMask : highMask;
        return ((mask >> (value & 63)) & 1) != 0;
    }

    private static bool IsRegexWordBoundary(ReadOnlySpan<char> text, int index)
    {
        var leftIsWord = index > 0 && IsRegexWordCharacter(text[index - 1]);
        var rightIsWord = index < text.Length && IsRegexWordCharacter(text[index]);
        return leftIsWord != rightIsWord;
    }

    private static bool IsRegexWordCharacter(char value)
    {
        const int wordCategories =
            (1 << (int)UnicodeCategory.UppercaseLetter)
            | (1 << (int)UnicodeCategory.LowercaseLetter)
            | (1 << (int)UnicodeCategory.TitlecaseLetter)
            | (1 << (int)UnicodeCategory.ModifierLetter)
            | (1 << (int)UnicodeCategory.OtherLetter)
            | (1 << (int)UnicodeCategory.NonSpacingMark)
            | (1 << (int)UnicodeCategory.DecimalDigitNumber)
            | (1 << (int)UnicodeCategory.ConnectorPunctuation);
        return value is '\u200C' or '\u200D'
            || ((1 << (int)CharUnicodeInfo.GetUnicodeCategory(value)) & wordCategories) != 0;
    }

    private static void VerifyAssignment(
        ReadOnlySpan<char> text,
        int anchorStart,
        ushort ruleIndex,
        ReadOnlySpan<char> provider,
        ulong classLowMask,
        ulong classHighMask,
        int normalMinimum,
        int earlyMinimum,
        int maximum,
        QuickLeaksScanMode mode,
        ref FindingSink sink
    )
    {
        if (
            provider.Length > text.Length - anchorStart
            || !text[anchorStart..].StartsWith(provider, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        var position = anchorStart + provider.Length;
        var contextLength = 0;
        while (
            position < text.Length
            && contextLength < 20
            && IsAssignmentContextCharacter(text[position])
        )
        {
            position++;
            contextLength++;
        }
        var quoteLength = 0;
        while (
            position < text.Length
            && quoteLength < 3
            && (char.IsWhiteSpace(text[position]) || text[position] is '\'' or '"')
        )
        {
            position++;
            quoteLength++;
        }

        Span<int> separatorLengths = stackalloc int[8];
        var separatorCount = GetAssignmentSeparatorLengths(text[position..], separatorLengths);
        var minimum = mode == QuickLeaksScanMode.Early ? earlyMinimum : normalMinimum;
        for (var separatorIndex = 0; separatorIndex < separatorCount; separatorIndex++)
        {
            var valuePrefix = position + separatorLengths[separatorIndex];
            var paddingLength = 0;
            while (
                valuePrefix + paddingLength < text.Length
                && paddingLength < 5
                && IsAssignmentPaddingCharacter(text[valuePrefix + paddingLength])
            )
            {
                paddingLength++;
            }

            for (var currentPadding = paddingLength; currentPadding >= 0; currentPadding--)
            {
                var valueStart = valuePrefix + currentPadding;
                var available = text.Length - valueStart;
                var maximumLength = maximum < 0 ? available : Math.Min(available, maximum);
                var valueLength = 0;
                while (
                    valueLength < maximumLength
                    && IsInAsciiCharacterClass(
                        text[valueStart + valueLength],
                        classLowMask,
                        classHighMask
                    )
                )
                {
                    valueLength++;
                }
                if (valueLength < minimum)
                {
                    continue;
                }
                var valueEnd = valueStart + valueLength;
                if (TryConsumeAssignmentTerminator(text, valueEnd, out var matchEnd))
                {
                    sink.Add(ruleIndex, anchorStart, matchEnd);
                    return;
                }
            }
        }
    }

    private static bool IsAssignmentContextCharacter(char value) =>
        value is ' ' or '\t' or '.' or '-' || IsRegexWordCharacter(value);

    private static bool IsAssignmentPaddingCharacter(char value) =>
        value is '`' or '\'' or '"' or '=' || char.IsWhiteSpace(value);

    private static int GetAssignmentSeparatorLengths(ReadOnlySpan<char> text, Span<int> lengths)
    {
        var count = 0;
        if (text.StartsWith("="))
        {
            lengths[count++] = 1;
        }
        if (text.StartsWith(">"))
        {
            lengths[count++] = 1;
        }
        if (!text.IsEmpty && text[0] == ':')
        {
            var colons = 0;
            while (colons < Math.Min(3, text.Length) && text[colons] == ':')
            {
                colons++;
            }
            for (var length = colons; length >= 1; length--)
            {
                if (length < text.Length && text[length] == '=')
                {
                    lengths[count++] = length + 1;
                }
            }
        }
        if (text.StartsWith("||"))
        {
            lengths[count++] = 2;
        }
        if (text.StartsWith(":"))
        {
            lengths[count++] = 1;
        }
        if (text.StartsWith("=>"))
        {
            lengths[count++] = 2;
        }
        if (text.StartsWith("?="))
        {
            lengths[count++] = 2;
        }
        if (text.StartsWith(","))
        {
            lengths[count++] = 1;
        }
        return count;
    }

    private static bool TryConsumeAssignmentTerminator(
        ReadOnlySpan<char> text,
        int position,
        out int matchEnd
    )
    {
        if (position == text.Length)
        {
            matchEnd = position;
            return true;
        }
        if (text[position] is '\'' or '"' or '`' or ';' || char.IsWhiteSpace(text[position]))
        {
            matchEnd = position + 1;
            return true;
        }
        if (
            text[position] == '\\'
            && position + 1 < text.Length
            && text[position + 1] is '\'' or '"' or '`' or 'n' or 'r'
        )
        {
            matchEnd = position + 2;
            return true;
        }
        matchEnd = position;
        return false;
    }

    private static void VerifyHomeDirectoryAt(
        ReadOnlySpan<char> text,
        int anchorStart,
        ushort ruleIndex,
        ref FindingSink sink
    )
    {
        if (anchorStart == 0)
        {
            return;
        }
        var isHome = text[anchorStart..].StartsWith("home", StringComparison.OrdinalIgnoreCase);
        var directoryLength = isHome ? 4 : 5;
        if (
            directoryLength > text.Length - anchorStart
            || anchorStart + directoryLength >= text.Length
        )
        {
            return;
        }
        var separator = text[anchorStart - 1];
        var nextSeparator = text[anchorStart + directoryLength];
        if (
            (isHome && (separator != '/' || nextSeparator != '/'))
            || (!isHome && (separator is not ('/' or '\\') || nextSeparator is not ('/' or '\\')))
        )
        {
            return;
        }

        var matchStart = anchorStart - 1;
        if (
            !isHome
            && anchorStart >= 3
            && text[anchorStart - 2] == ':'
            && IsAsciiLetter(text[anchorStart - 3])
        )
        {
            matchStart = anchorStart - 3;
        }
        var usernameEnd = anchorStart + directoryLength + 1;
        var usernameStart = usernameEnd;
        while (usernameEnd < text.Length && IsHomeDirectoryUsernameCharacter(text[usernameEnd]))
        {
            usernameEnd++;
        }
        if (usernameEnd > usernameStart)
        {
            sink.Add(ruleIndex, matchStart, usernameEnd);
        }
    }

    private static bool IsHomeDirectoryUsernameCharacter(char value) =>
        IsAsciiLetter(value) || char.IsAsciiDigit(value) || value is '.' or '_' or '-';

    private static void FindCredentialUriMatches(
        ReadOnlySpan<char> text,
        QuickLeaksScanMode mode,
        ushort ruleIndex,
        bool generic,
        ref FindingSink sink
    )
    {
        var searchOffset = 0;
        while (searchOffset < text.Length)
        {
            var relativeScheme = text[searchOffset..].IndexOf("://");
            if (relativeScheme < 0)
            {
                return;
            }

            var schemeEnd = searchOffset + relativeScheme;
            var schemeStart = schemeEnd;
            while (schemeStart > 0 && IsSchemeCharacter(text[schemeStart - 1]))
            {
                schemeStart--;
            }
            searchOffset = schemeEnd + 1;

            var schemeLength = schemeEnd - schemeStart;
            if (
                schemeLength is < 2 or > 21
                || !IsAsciiLetter(text[schemeStart])
                || (
                    schemeStart > 0
                    && (char.IsLetterOrDigit(text[schemeStart - 1]) || text[schemeStart - 1] == '_')
                )
            )
            {
                continue;
            }
            if (generic && !IsGenericCredentialScheme(text[schemeStart..schemeEnd]))
            {
                continue;
            }

            var usernameStart = schemeEnd + 3;
            var separator = usernameStart;
            while (
                separator < text.Length
                && separator - usernameStart <= 128
                && IsCredentialUsernameCharacter(text[separator], generic)
            )
            {
                separator++;
            }
            if (
                (!generic && separator == usernameStart)
                || separator - usernameStart > 128
                || separator >= text.Length
                || text[separator] != ':'
            )
            {
                continue;
            }

            var passwordStart = separator + 1;
            var passwordEnd = passwordStart;
            while (
                passwordEnd < text.Length
                && passwordEnd - passwordStart <= 256
                && IsCredentialPasswordCharacter(text[passwordEnd], generic)
            )
            {
                passwordEnd++;
            }

            var hasAt = passwordEnd < text.Length && text[passwordEnd] == '@';
            var hasEarlyBoundary =
                passwordEnd == text.Length
                || (passwordEnd < text.Length && char.IsWhiteSpace(text[passwordEnd]));
            if (generic)
            {
                if (
                    passwordEnd == passwordStart
                    || passwordEnd - passwordStart > 256
                    || !hasAt
                    || passwordEnd + 1 >= text.Length
                    || !IsGenericHostStart(text[passwordEnd + 1])
                )
                {
                    continue;
                }

                var uriEnd = passwordEnd + 2;
                while (uriEnd < text.Length && !IsCredentialUriTerminator(text[uriEnd]))
                {
                    uriEnd++;
                }
                sink.Add(ruleIndex, schemeStart, uriEnd);
                continue;
            }

            if (
                (mode == QuickLeaksScanMode.Normal && (passwordEnd == passwordStart || !hasAt))
                || (mode == QuickLeaksScanMode.Early && !hasAt && !hasEarlyBoundary)
            )
            {
                continue;
            }

            sink.Add(ruleIndex, schemeStart, hasAt ? passwordEnd + 1 : passwordEnd);
        }
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsSchemeCharacter(char value) =>
        IsAsciiLetter(value) || value is >= '0' and <= '9' or '+' or '.' or '-';

    private static bool IsCredentialUsernameCharacter(char value, bool generic) =>
        value is not ('/' or ':' or '@')
        && (!generic || value is not ('\'' or '"' or '`'))
        && !char.IsWhiteSpace(value);

    private static bool IsCredentialPasswordCharacter(char value, bool generic) =>
        value is not ('/' or '@')
        && (!generic || value is not ('\'' or '"' or '`'))
        && !char.IsWhiteSpace(value);

    private static bool IsGenericCredentialScheme(ReadOnlySpan<char> scheme) =>
        scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("mysql", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("mariadb", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("mongodb", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("mongodb+srv", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("redis", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("smtp", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("smtps", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("ftps", StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericHostStart(char value) =>
        value == '[' || char.IsAsciiLetterOrDigit(value);

    private static bool IsCredentialUriTerminator(char value) =>
        char.IsWhiteSpace(value)
        || value
            is '\''
                or '"'
                or '`'
                or '#'
                or '<'
                or '>'
                or '{'
                or '}'
                or '['
                or ']'
                or ','
                or ';'
                or ')';

    private static void FindCurlMatches(
        ReadOnlySpan<char> text,
        ushort ruleIndex,
        bool header,
        ref FindingSink sink
    )
    {
        var searchOffset = 0;
        while (searchOffset < text.Length)
        {
            var relative = text[searchOffset..].IndexOf("curl", StringComparison.Ordinal);
            if (relative < 0)
            {
                return;
            }

            var curlStart = searchOffset + relative;
            searchOffset = curlStart + 4;
            if (
                (curlStart > 0 && IsWordCharacter(text[curlStart - 1]))
                || (searchOffset < text.Length && IsWordCharacter(text[searchOffset]))
            )
            {
                continue;
            }

            var commandEnd = FindLineWindowEnd(text, searchOffset, 5);
            var command = text[curlStart..commandEnd];
            var optionSearchOffset = 0;
            while (optionSearchOffset < command.Length)
            {
                var optionOffset = FindCurlOption(command, optionSearchOffset, header);
                if (optionOffset < 0)
                {
                    break;
                }
                optionSearchOffset = optionOffset;

                var valueOffset = optionOffset;
                while (
                    valueOffset < command.Length
                    && (command[valueOffset] == '=' || char.IsWhiteSpace(command[valueOffset]))
                )
                {
                    valueOffset++;
                }
                if (valueOffset >= command.Length)
                {
                    break;
                }

                if (header)
                {
                    FindCurlHeader(command, curlStart, valueOffset, ruleIndex, ref sink);
                }
                else
                {
                    FindCurlUser(command, curlStart, valueOffset, ruleIndex, ref sink);
                }
            }
        }
    }

    private static int FindCurlOption(ReadOnlySpan<char> command, int searchOffset, bool header)
    {
        var longOption = FindCurlOptionEnd(
            command,
            searchOffset,
            header ? "--header" : "--user",
            allowAttachedArgument: false
        );
        var shortOption = FindCurlOptionEnd(
            command,
            searchOffset,
            header ? "-H" : "-u",
            allowAttachedArgument: true
        );
        if (longOption < 0)
        {
            return shortOption;
        }
        if (shortOption < 0)
        {
            return longOption;
        }
        var longOptionLength = header ? 8 : 6;
        return longOption - longOptionLength < shortOption - 2 ? longOption : shortOption;
    }

    private static int FindCurlOptionEnd(
        ReadOnlySpan<char> command,
        int searchOffset,
        string option,
        bool allowAttachedArgument
    )
    {
        var offset = searchOffset;
        while (offset < command.Length)
        {
            var relative = command[offset..].IndexOf(option, StringComparison.Ordinal);
            if (relative < 0)
            {
                return -1;
            }
            var start = offset + relative;
            var end = start + option.Length;
            if (
                start > 0
                && char.IsWhiteSpace(command[start - 1])
                && (
                    allowAttachedArgument
                    || end == command.Length
                    || command[end] == '='
                    || char.IsWhiteSpace(command[end])
                )
            )
            {
                return end;
            }
            offset = start + 1;
        }
        return -1;
    }

    private static void FindCurlHeader(
        ReadOnlySpan<char> command,
        int commandStart,
        int valueOffset,
        ushort ruleIndex,
        ref FindingSink sink
    )
    {
        var quote = command[valueOffset];
        if (quote is not ('\'' or '"'))
        {
            return;
        }
        var close = command[(valueOffset + 1)..].IndexOf(quote);
        if (close < 0)
        {
            return;
        }
        close += valueOffset + 1;
        var header = command[(valueOffset + 1)..close];
        var colon = header.IndexOf(':');
        if (colon <= 0)
        {
            return;
        }

        var name = header[..colon].Trim();
        var authorization = name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);
        if (
            !authorization
            && !name.EndsWith("Key", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith("Token", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        var tokenOffset = colon + 1;
        while (tokenOffset < header.Length && char.IsWhiteSpace(header[tokenOffset]))
        {
            tokenOffset++;
        }
        if (authorization)
        {
            var prefixLength = GetAuthorizationPrefixLength(header[tokenOffset..]);
            if (prefixLength > 0)
            {
                tokenOffset += prefixLength;
                while (tokenOffset < header.Length && char.IsWhiteSpace(header[tokenOffset]))
                {
                    tokenOffset++;
                }
            }
        }

        var tokenEnd = tokenOffset;
        while (tokenEnd < header.Length && IsCurlTokenCharacter(header[tokenEnd]))
        {
            tokenEnd++;
        }
        if (tokenEnd - tokenOffset >= 8)
        {
            sink.AddFinal(
                ruleIndex,
                commandStart + valueOffset + 1 + tokenOffset,
                commandStart + valueOffset + 1 + tokenEnd
            );
        }
    }

    private static void FindCurlUser(
        ReadOnlySpan<char> command,
        int commandStart,
        int valueOffset,
        ushort ruleIndex,
        ref FindingSink sink
    )
    {
        var quote = '\0';
        var separator = -1;
        var valueEnd = valueOffset;
        while (valueEnd < command.Length)
        {
            var value = command[valueEnd];
            if (value == ':' && separator < 0)
            {
                separator = valueEnd;
            }
            if (quote == '\0')
            {
                if (value is '\'' or '"')
                {
                    quote = value;
                }
                else if (char.IsWhiteSpace(value))
                {
                    break;
                }
            }
            else if (value == quote)
            {
                quote = '\0';
            }
            valueEnd++;
        }
        if (separator < 0)
        {
            return;
        }

        var usernameStart = valueOffset;
        var usernameEnd = separator;
        TrimCurlQuotes(command, ref usernameStart, ref usernameEnd);
        var passwordStart = separator + 1;
        var passwordEnd = valueEnd;
        TrimCurlQuotes(command, ref passwordStart, ref passwordEnd);
        if (usernameEnd - usernameStart >= 3)
        {
            sink.AddFinal(ruleIndex, commandStart + usernameStart, commandStart + usernameEnd);
        }
        if (passwordEnd - passwordStart >= 3)
        {
            sink.AddFinal(ruleIndex, commandStart + passwordStart, commandStart + passwordEnd);
        }
    }

    private static void TrimCurlQuotes(ReadOnlySpan<char> command, ref int start, ref int end)
    {
        if (start < end && command[start] is '\'' or '"')
        {
            start++;
        }
        if (start < end && command[end - 1] is '\'' or '"')
        {
            end--;
        }
    }

    private static int FindLineWindowEnd(ReadOnlySpan<char> text, int start, int followingLines)
    {
        var newlines = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '\n' && ++newlines > followingLines)
            {
                return index;
            }
        }
        return text.Length;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool IsCurlTokenCharacter(char value) =>
        char.IsLetterOrDigit(value)
        || value is '_' or '=' or '~' or '@' or '.' or '+' or '/' or '-';

    private static int GetAuthorizationPrefixLength(ReadOnlySpan<char> value)
    {
        if (HasAuthorizationPrefix(value, "Api-Token"))
        {
            return 9;
        }
        if (HasAuthorizationPrefix(value, "Bearer"))
        {
            return 6;
        }
        return HasAuthorizationPrefix(value, "Basic") || HasAuthorizationPrefix(value, "Token")
            ? 5
            : 0;
    }

    private static bool HasAuthorizationPrefix(ReadOnlySpan<char> value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && prefix.Length < value.Length
        && char.IsWhiteSpace(value[prefix.Length]);

    private enum FindingPostProcessor : byte
    {
        Default,
        GitIdentity,
        CredentialUri,
        HomeDirectory,
        GenericUsername,
    }

    private ref struct FindingSink
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly IBufferWriter<QuickLeaksFinding> _destination;

        public FindingSink(ReadOnlySpan<char> text, IBufferWriter<QuickLeaksFinding> destination)
        {
            _text = text;
            _destination = destination;
            Count = 0;
        }

        public int Count { get; private set; }

        public void AddFinal(ushort ruleIndex, int start, int end)
        {
            start = Math.Max(0, start);
            end = Math.Min(_text.Length, end);
            if (end > start)
            {
                Emit(ruleIndex, start, end);
            }
        }

        public void Add(ushort ruleIndex, int start, int end)
        {
            start = Math.Max(0, start);
            end = Math.Min(_text.Length, end);
            if (end <= start)
            {
                return;
            }

            switch (GetPostProcessor(ruleIndex))
            {
                case FindingPostProcessor.GitIdentity:
                    AddGitIdentity(ruleIndex, start, _text[start..end]);
                    return;
                case FindingPostProcessor.CredentialUri:
                    AddCredentialUri(ruleIndex, start, _text[start..end]);
                    return;
            }

            var narrowed = NarrowFinding(_text, ruleIndex, start, end, GetPostProcessor(ruleIndex));
            if (narrowed.End > narrowed.Start)
            {
                Emit(narrowed);
            }
        }

        private void AddGitIdentity(ushort ruleIndex, int start, ReadOnlySpan<char> span)
        {
            var openAngle = span.LastIndexOf('<');
            var at = openAngle >= 0 ? span[openAngle..].IndexOf('@') : -1;
            if (openAngle < 0 || at <= 1)
            {
                return;
            }

            at += openAngle;
            var identityStart = span[..openAngle].LastIndexOf(':') + 1;
            while (
                identityStart < openAngle
                && (span[identityStart] == ' ' || span[identityStart] == '\t')
            )
            {
                identityStart++;
            }

            var identityEnd = openAngle;
            while (
                identityEnd > identityStart
                && (span[identityEnd - 1] == ' ' || span[identityEnd - 1] == '\t')
            )
            {
                identityEnd--;
            }
            if (identityEnd > identityStart)
            {
                Emit(ruleIndex, start + identityStart, start + identityEnd);
            }

            Emit(ruleIndex, start + openAngle + 1, start + at);
        }

        private void AddCredentialUri(ushort ruleIndex, int start, ReadOnlySpan<char> span)
        {
            var schemeIndex = span.IndexOf("://".AsSpan(), StringComparison.Ordinal);
            if (schemeIndex < 0)
            {
                return;
            }

            var credentialsStart = schemeIndex + 3;
            var credentialsEnd = span.LastIndexOf('@');
            if (credentialsEnd < credentialsStart)
            {
                credentialsEnd = span.Length;
            }

            var credentials = span[credentialsStart..credentialsEnd];
            var separator = credentials.IndexOf(':');
            if (separator < 0)
            {
                if (!credentials.IsEmpty)
                {
                    Emit(ruleIndex, start + credentialsStart, start + credentialsEnd);
                }
                return;
            }

            if (separator > 0)
            {
                Emit(ruleIndex, start + credentialsStart, start + credentialsStart + separator);
            }

            var passwordStart = credentialsStart + separator + 1;
            if (passwordStart < credentialsEnd)
            {
                Emit(ruleIndex, start + passwordStart, start + credentialsEnd);
            }
        }

        private void Emit(ushort ruleIndex, int start, int end) =>
            Emit(new QuickLeaksFinding(GetRuleId(ruleIndex), start, end));

        private void Emit(QuickLeaksFinding finding)
        {
            var destination = _destination.GetSpan(1);
            destination[0] = finding;
            _destination.Advance(1);
            Count++;
        }
    }

    /// <summary>
    /// Narrows a raw regex match to the sensitive value portion, preserving
    /// prefixes such as <c>C:\Users\</c> or <c>PASSWORD=</c> for readability.
    /// </summary>
    private static QuickLeaksFinding NarrowFinding(
        ReadOnlySpan<char> text,
        ushort ruleIndex,
        int findingStart,
        int findingEnd,
        FindingPostProcessor postProcessor
    )
    {
        var ruleId = GetRuleId(ruleIndex);
        var start = Math.Max(0, findingStart);
        var end = Math.Min(text.Length, findingEnd);
        if (end <= start)
        {
            return new QuickLeaksFinding(ruleId, start, start);
        }

        var span = text.Slice(start, end - start);
        if (span.IsEmpty)
        {
            return new QuickLeaksFinding(ruleId, start, start);
        }

        if (postProcessor == FindingPostProcessor.HomeDirectory)
        {
            var lastSlash = span.LastIndexOf('/');
            var lastBackslash = span.LastIndexOf('\\');
            var lastSeparator = Math.Max(lastSlash, lastBackslash);
            if (lastSeparator >= 0 && lastSeparator + 1 < span.Length)
            {
                return new QuickLeaksFinding(ruleId, start + lastSeparator + 1, end);
            }
            return new QuickLeaksFinding(ruleId, start, start);
        }

        // Generic key=value secrets: mask only the value after '=' or ':'.
        // Token-only matches (no delimiter) keep the whole span.
        if (span.Contains("://".AsSpan(), StringComparison.Ordinal))
        {
            return new QuickLeaksFinding(ruleId, start, end);
        }

        var equalsIndex = span.IndexOf('=');
        var colonIndex = span.IndexOf(':');
        var delimiterIndex = equalsIndex >= 0 ? equalsIndex : colonIndex;
        if (delimiterIndex <= 0 || delimiterIndex >= span.Length - 1)
        {
            return new QuickLeaksFinding(ruleId, start, end);
        }

        var valueOffset = delimiterIndex + 1;
        while (valueOffset < span.Length && (span[valueOffset] == ' ' || span[valueOffset] == '\t'))
        {
            valueOffset++;
        }
        if (valueOffset >= span.Length)
        {
            return new QuickLeaksFinding(ruleId, start, start);
        }

        var quote = span[valueOffset];
        if (quote == '"' || quote == '\'' || quote == '`')
        {
            var valueStart = valueOffset + 1;
            var closingOffset = -1;
            for (var index = valueStart; index < span.Length; index++)
            {
                if (span[index] == quote && (index == valueStart || span[index - 1] != '\\'))
                {
                    closingOffset = index;
                    break;
                }
            }
            if (closingOffset >= 0)
            {
                if (closingOffset > valueStart)
                {
                    return new QuickLeaksFinding(ruleId, start + valueStart, start + closingOffset);
                }
                return new QuickLeaksFinding(ruleId, start, start);
            }
            // Unclosed quote (Early mode): mask to the end, trimming trailing whitespace.
            var trailingEnd = span.Length;
            while (
                trailingEnd > valueStart
                && (
                    span[trailingEnd - 1] == ' '
                    || span[trailingEnd - 1] == '\t'
                    || span[trailingEnd - 1] == '\r'
                    || span[trailingEnd - 1] == '\n'
                )
            )
            {
                trailingEnd--;
            }
            if (trailingEnd > valueStart)
            {
                return new QuickLeaksFinding(ruleId, start + valueStart, start + trailingEnd);
            }
            return new QuickLeaksFinding(ruleId, start, start);
        }

        var unquotedStart = valueOffset;
        // Skip stray delimiter characters from '=>', ':=', etc.
        while (
            unquotedStart < span.Length
            && (
                span[unquotedStart] == '='
                || span[unquotedStart] == ':'
                || span[unquotedStart] == '>'
            )
        )
        {
            unquotedStart++;
        }
        while (
            unquotedStart < span.Length
            && (span[unquotedStart] == ' ' || span[unquotedStart] == '\t')
        )
        {
            unquotedStart++;
        }
        if (unquotedStart >= span.Length)
        {
            return new QuickLeaksFinding(ruleId, start, start);
        }

        var unquotedEnd = unquotedStart;
        var isUsernameRule = postProcessor == FindingPostProcessor.GenericUsername;
        while (unquotedEnd < span.Length)
        {
            var character = span[unquotedEnd];
            if (
                character == ' '
                || character == '\t'
                || character == '\r'
                || character == '\n'
                || character == '"'
                || character == '\''
                || character == '`'
                || character == ','
                || character == ';'
                || character == ')'
                || character == ']'
                || character == '}'
                || (
                    isUsernameRule
                    && (
                        character == '@' || character == '/' || character == '?' || character == '#'
                    )
                )
            )
            {
                break;
            }
            unquotedEnd++;
        }
        if (unquotedEnd > unquotedStart)
        {
            return new QuickLeaksFinding(ruleId, start + unquotedStart, start + unquotedEnd);
        }
        return new QuickLeaksFinding(ruleId, start, start);
    }
}
