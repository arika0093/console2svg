using System;
using System.IO;
using System.Text.Json;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Loads <see cref="ChromeDefinition"/> instances from built-in embedded themes or custom JSON files.
/// </summary>
public static class ChromeLoader
{
    /// <summary>
    /// Loads a <see cref="ChromeDefinition"/> from a built-in theme ID.
    /// Returns <c>null</c> for <c>"none"</c>, <c>null</c>, or empty input.
    /// </summary>
    /// <param name="value">
    /// A built-in Chrome theme ID, or <c>"none"</c> / empty to disable chrome.
    /// </param>
    public static ChromeDefinition? Load(string? value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        var entry = new ThemeCatalog().Resolve(value);
        var appearance = entry.IsPcVariant
            ? entry.Manifest.Appearance?.Pc
            : entry.Manifest.Appearance?.Normal;
        if (string.IsNullOrWhiteSpace(appearance?.Window))
            throw new InvalidOperationException($"Theme '{value}' does not define window chrome.");
        var chrome = ThemeCatalog.ResolveChrome(entry, appearance.Window);
        if (chrome is not null && entry.IsPcVariant)
            chrome.IsDesktop = true;
        return chrome;
    }

    internal static ChromeDefinition Load(Stream stream) =>
        JsonSerializer.Deserialize(stream, ChromeDefinitionJsonContext.Default.ChromeDefinition)
        ?? throw new InvalidOperationException("Failed to parse chrome definition.");
}
