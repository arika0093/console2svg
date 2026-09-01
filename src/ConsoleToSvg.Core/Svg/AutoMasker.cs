using System;
using System.Collections.Generic;

namespace ConsoleToSvg.Svg;

internal sealed class AutoMasker
{
    private readonly AutoMaskCategory _categories;
    private readonly (string Parent, string UserName, StringComparison Comparison)[] _homePaths;

    public AutoMasker(AutoMaskCategory categories, string? homeDirectory)
    {
        _categories = categories;
        _homePaths = CreateHomePaths(categories, homeDirectory);
    }

    public bool IsEnabled => _categories != AutoMaskCategory.None;

    public bool[] CreateMask(string text)
    {
        var mask = new bool[text.Length];
        if ((_categories & (AutoMaskCategory.Password | AutoMaskCategory.Token)) != 0)
        {
            MarkKeyedSecrets(text, mask);
        }
        if ((_categories & AutoMaskCategory.HomeDirectory) != 0)
        {
            MarkHomeDirectoryUserNames(text, mask);
        }
        return mask;
    }

    public string Apply(string text)
    {
        var mask = CreateMask(text);
        char[]? result = null;
        for (var i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }
            result ??= text.ToCharArray();
            result[i] = '*';
        }
        return result is null ? text : new string(result);
    }

    private void MarkKeyedSecrets(string text, bool[] mask)
    {
        for (var separator = 0; separator < text.Length; separator++)
        {
            if (text[separator] is not ('=' or ':'))
            {
                continue;
            }

            var keyEnd = separator;
            while (keyEnd > 0 && char.IsWhiteSpace(text[keyEnd - 1]))
            {
                keyEnd--;
            }
            if (keyEnd > 0 && text[keyEnd - 1] is ('\'' or '"'))
            {
                keyEnd--;
            }

            var keyStart = keyEnd;
            while (keyStart > 0 && IsKeyCharacter(text[keyStart - 1]))
            {
                keyStart--;
            }
            if (keyStart == keyEnd || !IsSensitiveKey(text.AsSpan(keyStart, keyEnd - keyStart)))
            {
                continue;
            }

            var valueStart = separator + 1;
            while (valueStart < text.Length && char.IsWhiteSpace(text[valueStart]))
            {
                valueStart++;
            }

            var quote = '\0';
            if (valueStart < text.Length && text[valueStart] is ('\'' or '"'))
            {
                quote = text[valueStart];
                valueStart++;
            }

            var valueEnd = valueStart;
            while (valueEnd < text.Length)
            {
                var current = text[valueEnd];
                if (quote != '\0')
                {
                    if (current == quote)
                    {
                        if (IsBackslashEscaped(text, valueEnd))
                        {
                            valueEnd++;
                            continue;
                        }
                        if (valueEnd + 1 < text.Length && text[valueEnd + 1] == quote)
                        {
                            valueEnd += 2;
                            continue;
                        }
                        break;
                    }
                }
                else if (char.IsWhiteSpace(current) || current is ',' or ';' or '|' or '&')
                {
                    break;
                }
                valueEnd++;
            }

            Mark(mask, valueStart, valueEnd);
        }
    }

    private bool IsSensitiveKey(ReadOnlySpan<char> key)
    {
        if (
            _categories.HasFlag(AutoMaskCategory.Password)
            && key.EndsWith("PASSWORD", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }
        return _categories.HasFlag(AutoMaskCategory.Token)
            && key.EndsWith("TOKEN", StringComparison.OrdinalIgnoreCase);
    }

    private void MarkHomeDirectoryUserNames(string text, bool[] mask)
    {
        foreach (var homePath in _homePaths)
        {
            var searchStart = 0;
            while (searchStart < text.Length)
            {
                var parentIndex = text.IndexOf(homePath.Parent, searchStart, homePath.Comparison);
                if (parentIndex < 0)
                {
                    break;
                }

                var userStart = parentIndex + homePath.Parent.Length;
                var matched = 0;
                while (
                    matched < homePath.UserName.Length
                    && userStart + matched < text.Length
                    && CharactersEqual(
                        text[userStart + matched],
                        homePath.UserName[matched],
                        homePath.Comparison
                    )
                )
                {
                    matched++;
                }

                if (matched > 0)
                {
                    var complete = matched == homePath.UserName.Length;
                    var nextIndex = userStart + matched;
                    var isPartialAtLineEnd =
                        !complete
                        && (nextIndex >= text.Length || char.IsWhiteSpace(text[nextIndex]));
                    var hasUserNameBoundary =
                        complete
                        && (nextIndex >= text.Length || !IsUserNameCharacter(text[nextIndex]));
                    if (isPartialAtLineEnd || hasUserNameBoundary)
                    {
                        Mark(mask, userStart, nextIndex);
                    }
                }

                searchStart = userStart;
            }
        }
    }

    private static (string Parent, string UserName, StringComparison Comparison)[] CreateHomePaths(
        AutoMaskCategory categories,
        string? homeDirectory
    )
    {
        if (
            !categories.HasFlag(AutoMaskCategory.HomeDirectory)
            || string.IsNullOrWhiteSpace(homeDirectory)
        )
        {
            return [];
        }

        var trimmed = homeDirectory.TrimEnd('/', '\\');
        var separator = trimmed.LastIndexOfAny(['/', '\\']);
        if (separator < 0 || separator == trimmed.Length - 1)
        {
            return [];
        }

        var parent = trimmed[..(separator + 1)];
        var userName = trimmed[(separator + 1)..];
        var isWindowsPath =
            parent.Contains('\\', StringComparison.Ordinal)
            || (parent.Length >= 2 && parent[1] == ':');
        var comparison = isWindowsPath
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var paths = new List<(string, string, StringComparison)> { (parent, userName, comparison) };
        if (isWindowsPath)
        {
            var alternate = parent.Replace('\\', '/');
            if (!string.Equals(alternate, parent, StringComparison.Ordinal))
            {
                paths.Add((alternate, userName, comparison));
            }
        }
        return paths.ToArray();
    }

    private static bool IsKeyCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    private static bool IsUserNameCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    private static bool IsBackslashEscaped(string text, int index)
    {
        var backslashCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            backslashCount++;
        }
        return (backslashCount & 1) != 0;
    }

    private static bool CharactersEqual(char left, char right, StringComparison comparison) =>
        comparison == StringComparison.Ordinal
            ? left == right
            : char.ToUpperInvariant(left) == char.ToUpperInvariant(right);

    private static void Mark(bool[] mask, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            mask[i] = true;
        }
    }
}
