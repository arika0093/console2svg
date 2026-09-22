using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Batch;

public sealed class BatchAssetManifest
{
    public int Version { get; set; } = 1;
    public BatchAssetGenerator Generator { get; set; } = new();
    public Dictionary<string, BatchAssetObject> Objects { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, BatchLogicalAsset> Assets { get; set; } = new(StringComparer.Ordinal);
}

public sealed class BatchAssetGenerator
{
    public string Name { get; set; } = "console2svg";
    public string Version { get; set; } = string.Empty;
}

public sealed class BatchAssetObject
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string MediaType { get; set; } = "application/octet-stream";
}

public sealed class BatchLogicalAsset
{
    public string Object { get; set; } = string.Empty;
    public string[] Owners { get; set; } = [];
}

public sealed record BatchRestoreResult(
    int Restored,
    int Reused,
    int Filtered,
    int Materialized,
    int Pruned,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Failures
);

public static class BatchAssets
{
    private const string GeneratedDirectory = "generated";

    public static string ComputeRecipeHash(BatchParsedJob job, string extension)
    {
        var value = new StringBuilder("console2svg-recipe-v1");
        Add(value, job.Kind);
        Add(value, job.Setup);
        Add(value, job.Capture);
        Add(value, job.Teardown);
        Add(value, job.Width);
        Add(value, job.Height);
        Add(value, job.WithCommand);
        Add(value, job.Window);
        Add(value, job.WindowExplicit);
        Add(value, job.Video);
        Add(value, job.Timeout);

        var options = job.CaptureOptions;
        Add(value, options.Mode);
        Add(value, options.MaskAuto);
        Add(value, options.MaskPatterns);
        Add(value, options.Frame);
        Add(value, options.WidthAdjust);
        Add(value, options.HeightAdjust);
        Add(value, options.Time);
        Add(value, options.TimeStart);
        Add(value, options.TimeEnd);
        Add(value, options.CropTop);
        Add(value, options.CropRight);
        Add(value, options.CropBottom);
        Add(value, options.CropLeft);
        Add(value, options.Themes);
        Add(value, options.ForeColor);
        Add(value, options.IsForeColorExplicit);
        Add(value, options.Font);
        Add(value, options.IsFontExplicit);
        Add(value, options.FontSize);
        Add(value, options.IsFontSizeExplicit);
        Add(value, options.Window);
        Add(value, options.IsWindowExplicit);
        Add(value, options.Margin);
        Add(value, options.IsMarginExplicit);
        Add(value, options.Padding);
        Add(value, options.IsPaddingExplicit);
        Add(value, options.Loop);
        Add(value, options.VideoFps);
        Add(value, options.VideoSleep);
        Add(value, options.VideoFadeOut);
        Add(value, options.VideoTiming);
        Add(value, options.OutputCoalesceMs);
        Add(value, options.Opacity);
        Add(value, options.IsOpacityExplicit);
        Add(value, options.Prompt);
        Add(value, options.Header);
        Add(value, options.LengthAdjust);
        Add(value, options.Background);
        Add(value, options.IsBackgroundExplicit);
        Add(value, options.PcPadding);
        Add(value, options.PcMode);
        Add(value, options.BackColor);
        Add(value, options.IsBackColorExplicit);
        Add(value, options.NoColorEnv);
        Add(value, options.NoDeleteEnvs);
        Add(value, options.SizeWidth);
        Add(value, options.SizeHeight);
        Add(value, options.SvgConverter);
        Add(value, extension.ToLowerInvariant());

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    public static string GetCanonicalPath(string outputDir, string recipeHash, string extension) =>
        Path.Combine(outputDir, GeneratedDirectory, recipeHash + extension.ToLowerInvariant());

    public static void MaterializeAlias(
        string outputDir,
        string canonicalPath,
        string aliasPath,
        bool preserveExisting = false
    )
    {
        if (
            !IsSafeDestinationPath(outputDir, canonicalPath)
            || !IsSafeDestinationPath(outputDir, aliasPath)
        )
        {
            throw new InvalidDataException("Asset path escapes the output directory.");
        }

        if (Path.GetFullPath(canonicalPath) == Path.GetFullPath(aliasPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(aliasPath)!);
        if (PathExists(aliasPath))
        {
            if (preserveExisting)
            {
                return;
            }
            File.Delete(aliasPath);
        }

        var relativeTarget = Path.GetRelativePath(Path.GetDirectoryName(aliasPath)!, canonicalPath);
        try
        {
            File.CreateSymbolicLink(aliasPath, relativeTarget);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            File.Copy(canonicalPath, aliasPath, overwrite: false);
        }
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string GetMediaType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".cast" => "application/x-asciicast",
            _ => "application/octet-stream",
        };

    public static bool IsSafeDestinationPath(string outputDir, string path)
    {
        var root = Path.GetFullPath(outputDir);
        var fullPath = Path.GetFullPath(path);
        if (!BatchExecutor.IsPathInside(root, fullPath))
        {
            return false;
        }

        var current = Path.GetDirectoryName(fullPath);
        while (current is not null && !PathsEqual(current, root))
        {
            if (
                Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
            {
                return false;
            }
            current = Path.GetDirectoryName(current);
        }
        return current is not null;
    }

    public static async Task WriteManifestAsync(
        BatchAssetManifest manifest,
        string path,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer
                    .SerializeAsync(
                        stream,
                        manifest,
                        BatchAssetJsonContext.Default.BatchAssetManifest,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static async Task<BatchRestoreResult> RestoreAsync(
        string manifestSource,
        string outputDir,
        IReadOnlyList<string> filters,
        bool force,
        bool prune,
        bool dryRun,
        CancellationToken cancellationToken
    )
    {
        var failures = new List<string>();
        var actions = new List<string>();
        BatchAssetManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(manifestSource, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new BatchRestoreResult(0, 0, 0, 0, 0, [], [$"manifest: {ex.Message}"]);
        }

        outputDir = Path.GetFullPath(outputDir);
        if (!TryValidateManifest(manifest, outputDir, failures))
        {
            return new BatchRestoreResult(0, 0, 0, 0, 0, actions, failures);
        }

        var normalizedFilters = filters.Select(BatchExecutor.NormalizeFilter).ToArray();
        bool IsSelectedAsset(string logicalPath) =>
            normalizedFilters.Length == 0
            || normalizedFilters.Any(filter =>
                BatchExecutor.MatchesFilter(BatchExecutor.NormalizePath(logicalPath), filter)
            );

        BatchAssetManifest? previousManifest = null;
        var localManifestPath = Path.Combine(outputDir, "assets.json");
        if ((prune || normalizedFilters.Length > 0) && File.Exists(localManifestPath))
        {
            try
            {
                previousManifest = await ReadManifestAsync(localManifestPath, cancellationToken)
                    .ConfigureAwait(false);
                if (!TryValidateManifest(previousManifest, outputDir, failures))
                {
                    return new BatchRestoreResult(0, 0, 0, 0, 0, actions, failures);
                }
            }
            catch (Exception ex)
            {
                return new BatchRestoreResult(
                    0,
                    0,
                    0,
                    0,
                    0,
                    actions,
                    [$"existing assets.json: {ex.Message}"]
                );
            }
        }

        var selectedAssets = manifest.Assets.Where(pair => IsSelectedAsset(pair.Key)).ToArray();
        var filtered = manifest.Assets.Count - selectedAssets.Length;
        var requiredObjects = selectedAssets
            .Select(pair => pair.Value.Object)
            .ToHashSet(StringComparer.Ordinal);
        var restored = 0;
        var reused = 0;

        foreach (var objectId in requiredObjects.OrderBy(value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetObject = manifest.Objects[objectId];
            var destination = ResolveManifestPath(outputDir, assetObject.Path)!;
            var matches =
                !force
                && await MatchesAsync(destination, assetObject, cancellationToken)
                    .ConfigureAwait(false);
            if (matches)
            {
                reused++;
                actions.Add($"reuse {assetObject.Path}");
                continue;
            }
            actions.Add($"restore {assetObject.Path}");
            if (dryRun)
            {
                restored++;
                continue;
            }

            try
            {
                await RestoreOneAsync(manifestSource, assetObject, destination, cancellationToken)
                    .ConfigureAwait(false);
                restored++;
            }
            catch (Exception ex)
            {
                failures.Add($"{objectId}: {ex.Message}");
            }
        }

        var materialized = 0;
        if (failures.Count == 0)
        {
            foreach (var pair in selectedAssets.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var assetObject = manifest.Objects[pair.Value.Object];
                var canonical = ResolveManifestPath(outputDir, assetObject.Path)!;
                var logical = ResolveManifestPath(outputDir, pair.Key)!;
                materialized++;
                actions.Add($"materialize {pair.Key} -> {assetObject.Path}");
                if (dryRun)
                {
                    continue;
                }
                try
                {
                    MaterializeAlias(outputDir, canonical, logical);
                }
                catch (Exception ex)
                {
                    failures.Add($"{pair.Key}: could not materialize asset: {ex.Message}");
                }
            }
        }

        BatchAssetManifest manifestToWrite;
        if (normalizedFilters.Length == 0)
        {
            manifestToWrite = manifest;
        }
        else if (previousManifest is not null)
        {
            manifestToWrite = MergeManifests(previousManifest, manifest, IsSelectedAsset);
        }
        else
        {
            manifestToWrite = SelectManifest(manifest, IsSelectedAsset);
        }

        var pruned = 0;
        if (failures.Count == 0 && prune && previousManifest is not null)
        {
            var currentPaths = GetManagedPaths(manifestToWrite);
            var scopePaths = GetManagedPaths(SelectManifest(previousManifest, IsSelectedAsset));
            foreach (
                var stale in scopePaths
                    .Except(currentPaths, GetPathComparer())
                    .OrderByDescending(path => path, StringComparer.Ordinal)
            )
            {
                var path = ResolveManifestPath(outputDir, stale);
                if (path is null)
                {
                    failures.Add($"existing assets.json contains unsafe managed path '{stale}'.");
                    continue;
                }
                if (!PathExists(path))
                {
                    continue;
                }
                pruned++;
                actions.Add($"prune {stale}");
                if (!dryRun)
                {
                    File.Delete(path);
                }
            }
        }

        if (failures.Count == 0 && !dryRun)
        {
            await WriteManifestAsync(manifestToWrite, localManifestPath, cancellationToken)
                .ConfigureAwait(false);
        }

        return new BatchRestoreResult(
            restored,
            reused,
            filtered,
            materialized,
            pruned,
            actions,
            failures
        );
    }

    public static async Task<BatchAssetManifest> ReadManifestAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await OpenSourceAsync(source, cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer
                .DeserializeAsync(
                    stream,
                    BatchAssetJsonContext.Default.BatchAssetManifest,
                    cancellationToken
                )
                .ConfigureAwait(false)
            ?? throw new InvalidDataException("Manifest is empty.");
    }

    private static bool TryValidateManifest(
        BatchAssetManifest manifest,
        string outputDir,
        List<string> failures
    )
    {
        if (manifest.Version != 1)
        {
            failures.Add($"manifest: unsupported version {manifest.Version}.");
            return false;
        }
        if (manifest.Objects is null || manifest.Assets is null)
        {
            failures.Add("manifest: objects and assets are required.");
            return false;
        }

        var physicalPaths = new Dictionary<string, string>(GetPathComparer());
        foreach (var pair in manifest.Objects)
        {
            var value = pair.Value;
            if (
                value is null
                || string.IsNullOrWhiteSpace(pair.Key)
                || value.Size < 0
                || !IsSha256(value.Sha256)
            )
            {
                failures.Add($"{pair.Key}: invalid object metadata.");
                continue;
            }
            var path = ResolveManifestPath(outputDir, value.Path);
            if (path is null)
            {
                failures.Add($"{pair.Key}: unsafe object path '{value.Path}'.");
                continue;
            }
            if (!physicalPaths.TryAdd(path, pair.Key))
            {
                failures.Add($"{pair.Key}: object path is also used by '{physicalPaths[path]}'.");
            }
        }

        var logicalPaths = new HashSet<string>(GetPathComparer());
        foreach (var pair in manifest.Assets)
        {
            var value = pair.Value;
            if (
                value is null
                || string.IsNullOrWhiteSpace(value.Object)
                || !manifest.Objects.TryGetValue(value.Object, out var assetObject)
            )
            {
                failures.Add($"{pair.Key}: references an unknown object.");
                continue;
            }
            var path = ResolveManifestPath(outputDir, pair.Key);
            if (path is null)
            {
                failures.Add($"{pair.Key}: unsafe logical asset path.");
                continue;
            }
            if (!logicalPaths.Add(path))
            {
                failures.Add($"{pair.Key}: duplicate logical asset path.");
            }
            if (
                physicalPaths.TryGetValue(path, out var physicalOwner)
                && !string.Equals(physicalOwner, value.Object, StringComparison.Ordinal)
            )
            {
                failures.Add($"{pair.Key}: collides with object '{physicalOwner}'.");
            }
            _ = assetObject;
        }
        return failures.Count == 0;
    }

    private static HashSet<string> GetManagedPaths(BatchAssetManifest manifest)
    {
        var paths = manifest
            .Objects.Values.Select(value => BatchExecutor.NormalizePath(value.Path))
            .Concat(manifest.Assets.Keys.Select(BatchExecutor.NormalizePath));
        return paths.ToHashSet(GetPathComparer());
    }

    private static BatchAssetManifest SelectManifest(
        BatchAssetManifest manifest,
        Func<string, bool> isSelected
    )
    {
        var selected = manifest.Assets.Where(pair => isSelected(pair.Key)).ToArray();
        var objects = selected.Select(pair => pair.Value.Object).ToHashSet(StringComparer.Ordinal);
        return new BatchAssetManifest
        {
            Version = manifest.Version,
            Generator = manifest.Generator,
            Objects = manifest
                .Objects.Where(pair => objects.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Assets = selected.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal
            ),
        };
    }

    private static BatchAssetManifest MergeManifests(
        BatchAssetManifest previous,
        BatchAssetManifest incoming,
        Func<string, bool> isSelected
    )
    {
        var merged = new BatchAssetManifest
        {
            Version = incoming.Version,
            Generator = incoming.Generator,
        };
        foreach (var pair in previous.Assets.Where(pair => !isSelected(pair.Key)))
        {
            merged.Assets[pair.Key] = pair.Value;
            merged.Objects[pair.Value.Object] = previous.Objects[pair.Value.Object];
        }
        foreach (var pair in incoming.Assets.Where(pair => isSelected(pair.Key)))
        {
            merged.Assets[pair.Key] = pair.Value;
            merged.Objects[pair.Value.Object] = incoming.Objects[pair.Value.Object];
        }
        return merged;
    }

    private static async Task RestoreOneAsync(
        string manifestSource,
        BatchAssetObject entry,
        string destination,
        CancellationToken cancellationToken
    )
    {
        if (entry.Size < 0 || !IsSha256(entry.Sha256) || string.IsNullOrWhiteSpace(entry.Path))
        {
            throw new InvalidDataException("Manifest entry has invalid integrity metadata.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var input = await OpenAssetAsync(manifestSource, entry.Path, cancellationToken)
                    .ConfigureAwait(false)
            )
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while (
                    (read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false))
                    > 0
                )
                {
                    if (read > entry.Size - total)
                    {
                        throw new InvalidDataException(
                            $"size mismatch (expected {entry.Size}, received more)."
                        );
                    }
                    await output
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    total += read;
                }
            }

            var info = new FileInfo(temporary);
            if (info.Length != entry.Size)
            {
                throw new InvalidDataException(
                    $"size mismatch (expected {entry.Size}, received {info.Length})."
                );
            }
            var sha256 = await ComputeSha256Async(temporary, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SHA-256 mismatch.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> MatchesAsync(
        string path,
        BatchAssetObject entry,
        CancellationToken cancellationToken
    )
    {
        if (
            !File.Exists(path)
            || new FileInfo(path).Length != entry.Size
            || !IsSha256(entry.Sha256)
        )
        {
            return false;
        }
        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Stream> OpenSourceAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && IsHttp(uri))
        {
            return await SharedHttpClient
                .GetStreamAsync(uri, cancellationToken)
                .ConfigureAwait(false);
        }
        return File.OpenRead(Path.GetFullPath(source));
    }

    private static async Task<Stream> OpenAssetAsync(
        string manifestSource,
        string assetUrl,
        CancellationToken cancellationToken
    )
    {
        var remoteManifest =
            Uri.TryCreate(manifestSource, UriKind.Absolute, out var manifestUri)
            && IsHttp(manifestUri);
        if (Uri.TryCreate(assetUrl, UriKind.Absolute, out var absolute))
        {
            if (!IsHttp(absolute) && (!absolute.IsFile || remoteManifest))
            {
                throw new InvalidDataException(
                    remoteManifest
                        ? "A remote manifest asset URL must use HTTP(S)."
                        : "Asset URL must use HTTP(S) or file."
                );
            }
            return absolute.IsFile
                ? File.OpenRead(absolute.LocalPath)
                : await SharedHttpClient
                    .GetStreamAsync(absolute, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (remoteManifest)
        {
            var resolved = new Uri(manifestUri!, assetUrl);
            if (!IsHttp(resolved))
            {
                throw new InvalidDataException("Resolved asset URL must use HTTP(S).");
            }
            return await SharedHttpClient
                .GetStreamAsync(resolved, cancellationToken)
                .ConfigureAwait(false);
        }

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestSource))!;
        return File.OpenRead(Path.GetFullPath(assetUrl, manifestDirectory));
    }

    private static string? ResolveManifestPath(string outputDir, string relativePath)
    {
        var normalized = BatchExecutor.NormalizePath(relativePath);
        if (
            string.IsNullOrWhiteSpace(relativePath)
            || normalized.Equals("assets.json", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(relativePath)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _)
        )
        {
            return null;
        }
        var resolved = BatchExecutor.ResolveOutput(outputDir, relativePath);
        return
            resolved is not null
            && !PathsEqual(resolved, Path.Combine(outputDir, "assets.json"))
            && IsSafeDestinationPath(outputDir, resolved)
            ? resolved
            : null;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));

    private static bool IsHttp(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static bool PathsEqual(string first, string second) =>
        GetPathComparer().Equals(Path.GetFullPath(first), Path.GetFullPath(second));

    private static void Add(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => "<null>",
            IEnumerable<string> values => string.Join("\u001f", values),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        builder.Append('|').Append(text.Length).Append(':').Append(text);
    }

    private static readonly HttpClient SharedHttpClient = new();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true
)]
[JsonSerializable(typeof(BatchAssetManifest))]
internal sealed partial class BatchAssetJsonContext : JsonSerializerContext { }
