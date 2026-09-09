using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsoleToSvg.Terminal;

public sealed class ThemeManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("terminal")]
    public ThemeTerminal? Terminal { get; set; }

    [JsonPropertyName("appearance")]
    public ThemeAppearance? Appearance { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("$include")]
    public ThemeInclude[]? Includes { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ThemeManifest))]
[JsonSerializable(typeof(ThemeInstallation))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext { }

[JsonConverter(typeof(ThemeIncludeConverter))]
public sealed record ThemeInclude(string Id, string? Source = null);

public sealed class ThemeIncludeConverter : JsonConverter<ThemeInclude>
{
    public override ThemeInclude Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ThemeInclude(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("$include entries must be strings or objects.");
        string? id = null;
        string? source = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid $include entry.");
            var propertyName = reader.GetString();
            if (!reader.Read())
                throw new JsonException("Invalid $include entry.");
            switch (propertyName)
            {
                case "id":
                    id = reader.GetString();
                    break;
                case "source":
                    source = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(id))
            throw new JsonException("$include object entries require id.");
        return new ThemeInclude(id, source);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ThemeInclude value,
        JsonSerializerOptions options
    )
    {
        if (value.Source is null)
            writer.WriteStringValue(value.Id);
        else
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("source", value.Source);
            writer.WriteEndObject();
        }
    }
}

public sealed class ThemeTerminal
{
    [JsonPropertyName("foreground")]
    public string? Foreground { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("ansiPalette")]
    public string[]? AnsiPalette { get; set; }
}

public sealed class ThemeAppearance
{
    [JsonPropertyName("normal")]
    public ThemeVariant? Normal { get; set; }

    [JsonPropertyName("pc")]
    public ThemeVariant? Pc { get; set; }
}

public sealed class ThemeVariant
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("window")]
    public string? Window { get; set; }

    [JsonPropertyName("background")]
    public string[]? Background { get; set; }

    [JsonPropertyName("pcPadding")]
    public double? PcPadding { get; set; }

    [JsonPropertyName("margin")]
    public double? Margin { get; set; }

    [JsonPropertyName("padding")]
    public double? Padding { get; set; }

    [JsonPropertyName("opacity")]
    public double? Opacity { get; set; }

    [JsonPropertyName("font")]
    public string? Font { get; set; }

    [JsonPropertyName("fontSize")]
    public double? FontSize { get; set; }
}

public sealed record ThemeEntry(
    ThemeManifest Manifest,
    string Root,
    bool IsBuiltIn,
    bool IsPcVariant = false
);

public static class ThemeManifestValidator
{
    public static void Validate(ThemeManifest manifest, string root, bool validateAssets = true)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException("theme.json schemaVersion must be 1.");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !IsSafeId(manifest.Id))
            throw new InvalidDataException("theme.json id must be a safe non-empty identifier.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("theme.json name is required.");
        ValidateVariant(manifest.Appearance?.Normal, "normal");
        ValidateVariant(manifest.Appearance?.Pc, "pc");
        if (manifest.Terminal is { } terminal)
        {
            if (
                string.IsNullOrWhiteSpace(terminal.Foreground)
                || string.IsNullOrWhiteSpace(terminal.Background)
            )
                throw new InvalidDataException("terminal requires foreground and background.");
            if (terminal.AnsiPalette is not { Length: 16 })
                throw new InvalidDataException(
                    "terminal.ansiPalette must contain exactly 16 colors."
                );
        }
        if (
            manifest.Appearance?.Normal?.Background is { Length: > 2 }
            || manifest.Appearance?.Pc?.Background is { Length: > 2 }
        )
            throw new InvalidDataException("appearance.background may contain at most two values.");
        if (validateAssets)
            ValidateAsset(manifest.Preview, root);
        foreach (
            var window in new[]
            {
                manifest.Appearance?.Normal?.Window,
                manifest.Appearance?.Pc?.Window,
            }
        )
        {
            if (string.IsNullOrWhiteSpace(window))
                continue;
            if (
                !string.Equals(window, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(window, "macos", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(window, "macos-pc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(window, "windows", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(window, "windows-pc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(window, "transparent", StringComparison.OrdinalIgnoreCase)
                && validateAssets
            )
                ValidateAsset(window, root);
        }
        if (validateAssets)
            foreach (
                var value in new[]
                {
                    manifest.Appearance?.Normal?.Background,
                    manifest.Appearance?.Pc?.Background,
                }
                    .Where(background => background is not null)
                    .SelectMany(background => background!.Where(IsThemeRelativeImage))
            )
                ValidateAsset(value, root);
    }

    private static void ValidateVariant(ThemeVariant? variant, string name)
    {
        if (variant is not null && (string.IsNullOrWhiteSpace(variant.Id) || !IsSafeId(variant.Id)))
            throw new InvalidDataException(
                $"appearance.{name}.id must be a safe non-empty identifier."
            );
    }

    private static bool IsThemeRelativeImage(string value) =>
        !value.StartsWith("#", StringComparison.Ordinal)
        && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && Path.HasExtension(value);

    private static void ValidateAsset(string? relativePath, string root)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, fullRoot, StringComparison.Ordinal)
        )
            throw new InvalidDataException($"Theme asset escapes theme root: {relativePath}");
        if (!File.Exists(fullPath))
            throw new InvalidDataException($"Theme asset not found: {relativePath}");
    }

    private static bool IsSafeId(string value) =>
        value.IndexOfAny(['/', '\\', ':']) < 0 && value != "." && value != "..";
}
