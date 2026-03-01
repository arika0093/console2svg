using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ConsoleToSvg.Svg;

/// <summary>
/// Loads <see cref="ChromeDefinition"/> instances from built-in embedded themes or custom JSON files.
/// </summary>
public static class ChromeLoader
{
    private static readonly string[] BuiltinNames = ["macos", "macos-pc", "windows", "windows-pc"];

    /// <summary>
    /// Loads a <see cref="ChromeDefinition"/> from a built-in name or a file path.
    /// Returns <c>null</c> for <c>"none"</c>, <c>null</c>, or empty input.
    /// </summary>
    /// <param name="value">
    /// A built-in style name (<c>macos</c>, <c>windows</c>, <c>macos-pc</c>, <c>windows-pc</c>),
    /// a path to a custom <c>.json</c> chrome definition file,
    /// or <c>"none"</c> / empty to disable chrome.
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

        var builtinName = Array.Find(
            BuiltinNames,
            name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)
        );
        if (builtinName is not null)
        {
            return LoadBuiltin(builtinName);
        }

        return LoadFromFile(value);
    }

    private static ChromeDefinition LoadBuiltin(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"chrome.{name}.json";
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Built-in chrome theme '{name}' could not be found. "
                    + $"Expected embedded resource: {resourceName}"
            );

        return JsonSerializer.Deserialize(
                stream,
                ChromeDefinitionJsonContext.Default.ChromeDefinition
            )
            ?? throw new InvalidOperationException(
                $"Failed to parse built-in chrome theme '{name}'."
            );
    }

    private static ChromeDefinition LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Chrome definition file not found: '{path}'.", path);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(
                stream,
                ChromeDefinitionJsonContext.Default.ChromeDefinition
            )
            ?? throw new InvalidOperationException(
                $"Failed to parse chrome definition file: '{path}'."
            );
    }
}
