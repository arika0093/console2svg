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
    public string Generator { get; set; } = string.Empty;
    public Dictionary<string, BatchAssetManifestEntry> Assets { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class BatchAssetManifestEntry
{
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string MediaType { get; set; } = "application/octet-stream";
    public string Url { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = [];
    public string? Recipe { get; set; }
}

public sealed record BatchRestoreResult(
    int Restored,
    int Reused,
    int Filtered,
    int Removed,
    IReadOnlyList<string> Failures
);

public static class BatchAssets
{
    private const string GeneratedDirectory = ".generated";

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
        BatchAssetManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(manifestSource, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new BatchRestoreResult(0, 0, 0, 0, [$"manifest: {ex.Message}"]);
        }

        if (manifest.Version != 1)
        {
            return new BatchRestoreResult(
                0,
                0,
                0,
                0,
                [$"manifest: unsupported version {manifest.Version}."]
            );
        }
        if (manifest.Assets is null)
        {
            return new BatchRestoreResult(0, 0, 0, 0, ["manifest: assets is required."]);
        }

        outputDir = Path.GetFullPath(outputDir);
        var declared = new HashSet<string>(GetPathComparer());
        var owners = new Dictionary<string, string>(GetPathComparer());
        var resolvedPaths = new Dictionary<string, (string Destination, string[] Aliases)>(
            StringComparer.Ordinal
        );
        var normalizedFilters = filters.Select(BatchExecutor.NormalizeFilter).ToArray();
        var restored = 0;
        var reused = 0;
        var filtered = 0;

        foreach (var pair in manifest.Assets)
        {
            if (pair.Value is null)
            {
                failures.Add($"{pair.Key}: manifest entry is required.");
                continue;
            }
            if (
                pair.Value.Size < 0
                || !IsSha256(pair.Value.Sha256)
                || string.IsNullOrWhiteSpace(pair.Value.Url)
            )
            {
                failures.Add($"{pair.Key}: invalid integrity metadata.");
                continue;
            }

            var destination = ResolveManifestPath(outputDir, pair.Key);
            if (destination is null)
            {
                failures.Add($"{pair.Key}: unsafe destination path.");
                continue;
            }

            var aliases = new List<string>();
            foreach (var alias in pair.Value.Aliases ?? [])
            {
                var aliasPath = ResolveManifestPath(outputDir, alias);
                if (aliasPath is null)
                {
                    failures.Add($"{pair.Key}: unsafe alias path '{alias}'.");
                    continue;
                }
                aliases.Add(aliasPath);
            }
            if (failures.Count > 0)
            {
                continue;
            }

            foreach (var path in new[] { destination }.Concat(aliases))
            {
                if (owners.TryGetValue(path, out var owner) && owner != pair.Key)
                {
                    failures.Add($"{pair.Key}: output path is also declared by '{owner}'.");
                }
                else
                {
                    owners[path] = pair.Key;
                    declared.Add(path);
                }
            }
            resolvedPaths[pair.Key] = (destination, aliases.ToArray());
        }

        if (failures.Count > 0)
        {
            return new BatchRestoreResult(0, 0, 0, 0, failures);
        }

        foreach (var pair in manifest.Assets.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = new[] { pair.Key }.Concat(pair.Value.Aliases ?? []).ToArray();
            if (
                normalizedFilters.Length > 0
                && !paths.Any(path =>
                    normalizedFilters.Any(filter =>
                        BatchExecutor.MatchesFilter(BatchExecutor.NormalizePath(path), filter)
                    )
                )
            )
            {
                filtered++;
                continue;
            }

            var (destination, aliases) = resolvedPaths[pair.Key];

            var matches =
                !force
                && await MatchesAsync(destination, pair.Value, cancellationToken)
                    .ConfigureAwait(false);
            if (matches)
            {
                reused++;
            }
            else if (dryRun)
            {
                restored++;
            }
            else
            {
                try
                {
                    await RestoreOneAsync(
                            manifestSource,
                            pair.Value,
                            destination,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    restored++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{pair.Key}: {ex.Message}");
                    continue;
                }
            }

            if (!dryRun)
            {
                try
                {
                    foreach (var alias in aliases)
                    {
                        MaterializeAlias(outputDir, destination, alias);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{pair.Key}: could not materialize aliases: {ex.Message}");
                }
            }
        }

        var removed = 0;
        if (prune && Directory.Exists(outputDir))
        {
            foreach (var path in EnumerateFilesWithoutFollowingLinks(outputDir))
            {
                if (declared.Contains(Path.GetFullPath(path)))
                {
                    continue;
                }
                removed++;
                if (!dryRun)
                {
                    File.Delete(path);
                }
            }
        }

        return new BatchRestoreResult(restored, reused, filtered, removed, failures);
    }

    private static async Task<BatchAssetManifest> ReadManifestAsync(
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

    private static async Task RestoreOneAsync(
        string manifestSource,
        BatchAssetManifestEntry entry,
        string destination,
        CancellationToken cancellationToken
    )
    {
        if (entry.Size < 0 || !IsSha256(entry.Sha256) || string.IsNullOrWhiteSpace(entry.Url))
        {
            throw new InvalidDataException("Manifest entry has invalid integrity metadata.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var input = await OpenAssetAsync(manifestSource, entry.Url, cancellationToken)
                    .ConfigureAwait(false)
            )
            await using (var output = File.Create(temporary))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
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
        BatchAssetManifestEntry entry,
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
        if (
            string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _)
        )
        {
            return null;
        }
        var resolved = BatchExecutor.ResolveOutput(outputDir, relativePath);
        return resolved is not null && IsSafeDestinationPath(outputDir, resolved) ? resolved : null;
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

    private static IEnumerable<string> EnumerateFilesWithoutFollowingLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

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
