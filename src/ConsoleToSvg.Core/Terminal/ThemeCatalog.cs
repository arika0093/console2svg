using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Terminal;

public sealed class ThemeCatalog
{
    private readonly Dictionary<string, ThemeEntry> _entries;

    public ThemeCatalog()
    {
        _entries = new Dictionary<string, ThemeEntry>(StringComparer.OrdinalIgnoreCase);
        LoadBuiltIns();
        LoadInstalled();
    }

    public IEnumerable<ThemeEntry> Entries => _entries.Values.SelectMany(ExpandVariants);

    public ThemeEntry Resolve(string id)
    {
        if (_entries.ContainsKey(id))
            return Resolve(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var candidate in _entries.Values)
        {
            var resolved = Resolve(
                candidate.Manifest.Id!,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            );
            if (
                string.Equals(
                    resolved.Manifest.Appearance?.Normal?.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return CreateVariantEntry(resolved, resolved.Manifest.Appearance!.Normal!, false);
            if (
                string.Equals(
                    resolved.Manifest.Appearance?.Pc?.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return CreateVariantEntry(resolved, resolved.Manifest.Appearance!.Pc!, true);
        }
        throw new InvalidOperationException($"Theme not found: '{id}'.");
    }

    public static ChromeDefinition? ResolveChrome(ThemeEntry entry, string path)
    {
        if (string.Equals(path, "none", StringComparison.OrdinalIgnoreCase))
            return null;
        var resource = entry.Root + "/" + path;
        ChromeDefinition chrome;
        string? assetRoot;
        if (!entry.IsBuiltIn)
        {
            var chromePath = Path.Combine(entry.Root, path);
            using var file = File.OpenRead(chromePath);
            chrome = ChromeLoader.Load(file);
            assetRoot = Path.GetDirectoryName(chromePath);
        }
        else
        {
            var stream = OpenBuiltInResource(resource, path);
            using (stream)
                chrome = ChromeLoader.Load(stream);
            assetRoot = resource[..resource.LastIndexOf('/')];
        }
        if (!string.IsNullOrWhiteSpace(chrome.SvgTemplate))
            chrome.SvgTemplate = LoadTemplate(entry, assetRoot!, chrome.SvgTemplate);
        chrome.DesktopSvgTemplate = LoadOptionalTemplate(
            entry,
            assetRoot!,
            chrome.DesktopSvgTemplate
        );
        return chrome;
    }

    public static string[]? ResolveBackground(ThemeEntry entry, string[]? background) =>
        background?.Select(value => ResolveBackgroundValue(entry, value)).ToArray();

    private static string ResolveBackgroundValue(ThemeEntry entry, string value)
    {
        if (!IsThemeRelativeImage(value))
            return value;
        if (!entry.IsBuiltIn)
            return Path.GetFullPath(Path.Combine(entry.Root, value));
        var resource = entry.Root + "/" + value.TrimStart('.', '/').Replace('\\', '/');
        var stream = OpenBuiltInResource(resource, value);
        using (stream)
        using (var data = new MemoryStream())
        {
            stream.CopyTo(data);
            return $"data:{GetImageMimeType(value)};base64,{Convert.ToBase64String(data.ToArray())}";
        }
    }

    private static bool IsThemeRelativeImage(string value) =>
        !value.StartsWith("#", StringComparison.Ordinal)
        && !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && Path.HasExtension(value);

    private static string GetImageMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };

    private static string LoadTemplate(ThemeEntry entry, string assetRoot, string path)
    {
        if (path.StartsWith("<", StringComparison.Ordinal))
            throw new InvalidDataException("Chrome svgTemplate must be an SVG asset path.");
        return LoadAssetText(entry, assetRoot, path);
    }

    private static string? LoadOptionalTemplate(ThemeEntry entry, string assetRoot, string? path) =>
        string.IsNullOrWhiteSpace(path) ? path : LoadTemplate(entry, assetRoot, path);

    private static string LoadAssetText(ThemeEntry entry, string assetRoot, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Chrome asset escapes theme root: {path}");
        if (!entry.IsBuiltIn)
            return File.ReadAllText(Path.Combine(assetRoot, path));
        var resource = assetRoot + "/" + path;
        var stream = OpenBuiltInResource(resource, path);
        using (stream)
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }

    public static string UserThemeDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "console2svg",
            "themes"
        );

    public static string InstallationDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "console2svg",
            "installations"
        );

    private void LoadBuiltIns()
    {
        var assembly = typeof(ThemeCatalog).Assembly;
        foreach (
            var resource in assembly
                .GetManifestResourceNames()
                .Where(x =>
                    NormalizeResourceName(x)
                        .StartsWith("theme/", StringComparison.OrdinalIgnoreCase)
                    && NormalizeResourceName(x)
                        .EndsWith("/theme.json", StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var manifest =
                JsonSerializer.Deserialize<ThemeManifest>(stream)
                ?? throw new InvalidDataException(resource);
            var normalizedResource = NormalizeResourceName(resource);
            var root = normalizedResource[
                ..normalizedResource.LastIndexOf("/theme.json", StringComparison.OrdinalIgnoreCase)
            ];
            ThemeManifestValidator.Validate(manifest, root, validateAssets: false);
            _entries.Add(manifest.Id!, new ThemeEntry(manifest, root, true));
        }
    }

    private static Stream OpenBuiltInResource(string resource, string assetPath)
    {
        var assembly = typeof(ThemeCatalog).Assembly;
        var actualName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                string.Equals(
                    NormalizeResourceName(name),
                    resource,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        return actualName is null
            ? throw new InvalidDataException($"Theme asset not found: {assetPath}")
            : assembly.GetManifestResourceStream(actualName)!;
    }

    private static string NormalizeResourceName(string value) => value.Replace('\\', '/');

    private ThemeEntry Resolve(string id, HashSet<string> resolving)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"Theme not found: '{id}'.");
        if (!resolving.Add(id))
            throw new InvalidDataException($"Theme include cycle detected at '{id}'.");
        var manifest = entry.Manifest;
        if (manifest.Includes is { Length: > 0 })
        {
            var merged = new ThemeManifest
            {
                SchemaVersion = manifest.SchemaVersion,
                Id = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                Description = manifest.Description,
                Author = manifest.Author,
                License = manifest.License,
                Preview = manifest.Preview,
            };
            foreach (var include in manifest.Includes)
            {
                var included = Resolve(include.Id, resolving).Manifest;
                MergeInto(merged, included);
            }
            MergeInto(merged, manifest);
            entry = entry with { Manifest = merged };
        }
        resolving.Remove(id);
        return entry;
    }

    private static void MergeInto(ThemeManifest target, ThemeManifest source)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.Id = source.Id ?? target.Id;
        target.Name = source.Name ?? target.Name;
        target.Version = source.Version ?? target.Version;
        target.Description = source.Description ?? target.Description;
        target.Author = source.Author ?? target.Author;
        target.License = source.License ?? target.License;
        target.Preview = source.Preview ?? target.Preview;
        if (source.Terminal is not null)
            target.Terminal = new ThemeTerminal
            {
                Foreground = source.Terminal.Foreground ?? target.Terminal?.Foreground,
                Background = source.Terminal.Background ?? target.Terminal?.Background,
                AnsiPalette = source.Terminal.AnsiPalette ?? target.Terminal?.AnsiPalette,
            };
        if (source.Appearance is not null)
            target.Appearance = new ThemeAppearance
            {
                Normal = MergeVariant(target.Appearance?.Normal, source.Appearance.Normal),
                Pc = MergeVariant(target.Appearance?.Pc, source.Appearance.Pc),
            };
    }

    private static ThemeVariant? MergeVariant(ThemeVariant? target, ThemeVariant? source)
    {
        if (source is null)
            return target;
        target ??= new ThemeVariant();
        target.Id = source.Id ?? target.Id;
        target.Window = source.Window ?? target.Window;
        target.PcPadding = source.PcPadding ?? target.PcPadding;
        target.Margin = source.Margin ?? target.Margin;
        target.Padding = source.Padding ?? target.Padding;
        target.Opacity = source.Opacity ?? target.Opacity;
        target.Background = source.Background ?? target.Background;
        target.Font = source.Font ?? target.Font;
        target.FontSize = source.FontSize ?? target.FontSize;
        return target;
    }

    private static IEnumerable<ThemeEntry> ExpandVariants(ThemeEntry entry)
    {
        if (
            entry.Manifest.Appearance?.Normal is { } normal
            && !string.Equals(normal.Id, entry.Manifest.Id, StringComparison.OrdinalIgnoreCase)
        )
            yield return CreateVariantEntry(entry, normal, false);
        yield return entry;
        if (entry.Manifest.Appearance?.Pc is { } pc)
            yield return CreateVariantEntry(entry, pc, true);
    }

    private static ThemeEntry CreateVariantEntry(
        ThemeEntry entry,
        ThemeVariant variant,
        bool isPcVariant
    ) =>
        entry with
        {
            Manifest = new ThemeManifest
            {
                SchemaVersion = entry.Manifest.SchemaVersion,
                Id = variant.Id,
                Name = entry.Manifest.Name,
                Version = entry.Manifest.Version,
                Description = entry.Manifest.Description,
                Author = entry.Manifest.Author,
                License = entry.Manifest.License,
                Terminal = entry.Manifest.Terminal,
                Appearance = isPcVariant
                    ? new ThemeAppearance { Pc = variant }
                    : new ThemeAppearance { Normal = variant },
                Preview = entry.Manifest.Preview,
            },
            IsPcVariant = isPcVariant,
        };

    private void LoadInstalled()
    {
        if (!Directory.Exists(UserThemeDirectory))
            return;
        foreach (var directory in Directory.EnumerateDirectories(UserThemeDirectory))
        {
            var path = Path.Combine(directory, "theme.json");
            if (!File.Exists(path))
                continue;
            var manifest =
                JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(path))
                ?? throw new InvalidDataException(path);
            ThemeManifestValidator.Validate(manifest, directory);
            if (!_entries.ContainsKey(manifest.Id!))
                _entries.Add(manifest.Id!, new ThemeEntry(manifest, directory, false));
        }
    }
}
