using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ConsoleToSvg.Terminal;

public static class ThemeManager
{
    public static ThemeEntry Install(string sourceText)
    {
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "console2svg-" + Guid.NewGuid().ToString("N")
        );
        var staged = Path.Combine(
            ThemeCatalog.UserThemeDirectory,
            ".staging-" + Guid.NewGuid().ToString("N")
        );
        var backups = new List<(string Destination, string Backup)>();
        Directory.CreateDirectory(temporary);
        try
        {
            var catalog = new ThemeCatalog();
            var themes = new Dictionary<string, StagedTheme>(StringComparer.OrdinalIgnoreCase);
            CollectTheme(
                ThemeSource.Parse(sourceText),
                null,
                temporary,
                catalog,
                themes,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            );
            Directory.CreateDirectory(staged);
            foreach (var theme in themes.Values)
            {
                var directory = Path.Combine(staged, "themes", theme.Manifest.Id!);
                CopyDirectory(theme.Root, directory);
                File.WriteAllText(
                    Path.Combine(staged, theme.Manifest.Id + ".json"),
                    JsonSerializer.Serialize(
                        new ThemeInstallation(theme.Source, theme.Manifest.Id!, theme.Commit),
                        ThemeJsonContext.Default.ThemeInstallation
                    )
                );
            }

            Directory.CreateDirectory(ThemeCatalog.UserThemeDirectory);
            Directory.CreateDirectory(ThemeCatalog.InstallationDirectory);
            try
            {
                foreach (var id in themes.Values.Select(theme => theme.Manifest.Id!))
                {
                    PublishDirectory(
                        Path.Combine(staged, "themes", id),
                        Path.Combine(ThemeCatalog.UserThemeDirectory, id),
                        backups
                    );
                    PublishFile(
                        Path.Combine(staged, id + ".json"),
                        Path.Combine(ThemeCatalog.InstallationDirectory, id + ".json"),
                        backups
                    );
                }
            }
            catch
            {
                RestoreBackups(backups);
                throw;
            }
            return new ThemeCatalog().Resolve(themes.Values.Single(x => x.IsRoot).Manifest.Id!);
        }
        finally
        {
            if (Directory.Exists(temporary))
                RepositorySourceAcquirer.DeleteCheckout(temporary);
            if (Directory.Exists(staged))
                Directory.Delete(staged, true);
            foreach (var backup in backups.Select(item => item.Backup))
                if (Directory.Exists(backup))
                    Directory.Delete(backup, true);
                else if (File.Exists(backup))
                    File.Delete(backup);
        }
    }

    public static void Remove(string id)
    {
        var entry = new ThemeCatalog().Resolve(id);
        if (entry.IsBuiltIn)
            throw new InvalidOperationException($"Built-in theme '{id}' cannot be removed.");
        Directory.Delete(entry.Root, true);
        var metadata = Path.Combine(ThemeCatalog.InstallationDirectory, id + ".json");
        if (File.Exists(metadata))
            File.Delete(metadata);
    }

    public static void Update(string? id = null)
    {
        var files = Directory.Exists(ThemeCatalog.InstallationDirectory)
            ? Directory.EnumerateFiles(ThemeCatalog.InstallationDirectory, "*.json")
            : [];
        foreach (var file in files)
        {
            var installation = JsonSerializer.Deserialize(
                File.ReadAllText(file),
                ThemeJsonContext.Default.ThemeInstallation
            );
            if (
                installation is null
                || (
                    id is not null
                    && !string.Equals(id, installation.Id, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            UpdateInstalledTheme(installation, file);
        }
        if (
            id is not null
            && !File.Exists(Path.Combine(ThemeCatalog.InstallationDirectory, id + ".json"))
        )
            throw new InvalidOperationException($"Installed theme not found: '{id}'.");
    }

    private static void CollectTheme(
        ThemeSource source,
        string? expectedId,
        string temporary,
        ThemeCatalog catalog,
        Dictionary<string, StagedTheme> themes,
        HashSet<string> resolving
    )
    {
        var clone = Path.Combine(temporary, Guid.NewGuid().ToString("N"));
        var checkout = RepositorySourceAcquirer.Acquire(source.ToRepositorySource(), clone);
        var root = checkout.Root;
        var manifest = ReadManifest(root);
        if (
            expectedId is not null
            && !string.Equals(manifest.Id, expectedId, StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidDataException(
                $"Dependency source declares '{manifest.Id}', expected '{expectedId}'."
            );
        AddTheme(
            source,
            root,
            manifest,
            checkout.Commit,
            expectedId is null,
            catalog,
            themes,
            resolving,
            temporary
        );
    }

    private static void AddTheme(
        ThemeSource source,
        string root,
        ThemeManifest manifest,
        string commit,
        bool isRoot,
        ThemeCatalog catalog,
        Dictionary<string, StagedTheme> themes,
        HashSet<string> resolving,
        string temporary
    )
    {
        var id = manifest.Id!;
        if (
            catalog.Entries.Any(x =>
                x.IsBuiltIn && string.Equals(x.Manifest.Id, id, StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new InvalidOperationException($"Cannot overwrite built-in theme '{id}'.");
        if (!resolving.Add(id))
            throw new InvalidDataException($"Theme dependency cycle detected at '{id}'.");
        if (themes.TryGetValue(id, out var existing))
        {
            if (existing.Source != source)
                throw new InvalidDataException($"Conflicting dependency definitions for '{id}'.");
            resolving.Remove(id);
            return;
        }
        themes.Add(id, new StagedTheme(source, root, manifest, commit, isRoot));
        foreach (var include in manifest.Includes ?? [])
        {
            if (
                catalog.Entries.Any(x =>
                    string.Equals(x.Manifest.Id, include.Id, StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            if (include.Source is not null)
                CollectTheme(
                    ThemeSource.Parse(include.Source),
                    include.Id,
                    temporary,
                    catalog,
                    themes,
                    resolving
                );
            else
            {
                var dependencyRoot = FindThemeRoot(root, include.Id);
                AddTheme(
                    source,
                    dependencyRoot,
                    ReadManifest(dependencyRoot),
                    commit,
                    false,
                    catalog,
                    themes,
                    resolving,
                    temporary
                );
            }
        }
        resolving.Remove(id);
    }

    private static string FindThemeRoot(string sourceRoot, string id)
    {
        var match = Directory
            .EnumerateFiles(
                FindRepositoryRoot(sourceRoot),
                "theme.json",
                SearchOption.AllDirectories
            )
            .Select(path => Path.GetDirectoryName(path)!)
            .FirstOrDefault(path =>
                string.Equals(ReadManifest(path).Id, id, StringComparison.OrdinalIgnoreCase)
            );
        return match
            ?? throw new InvalidDataException(
                $"Dependency theme '{id}' was not found in the source repository."
            );
    }

    private static string FindRepositoryRoot(string root)
    {
        var directory = new DirectoryInfo(root);
        while (
            directory.Parent is not null
            && !Directory.Exists(Path.Combine(directory.FullName, ".git"))
        )
            directory = directory.Parent;
        return directory.FullName;
    }

    private static void UpdateInstalledTheme(ThemeInstallation installation, string metadata)
    {
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "console2svg-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(temporary);
        try
        {
            var checkout = RepositorySourceAcquirer.Acquire(
                installation.Source.ToRepositorySource(),
                temporary
            );
            var root = checkout.Root;
            var manifest = ReadManifest(root);
            if (!string.Equals(manifest.Id, installation.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Updated theme ID does not match installed ID.");
            var destination = Path.Combine(ThemeCatalog.UserThemeDirectory, installation.Id);
            var staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            CopyDirectory(root, staging);
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            Directory.Move(staging, destination);
            File.WriteAllText(
                metadata,
                JsonSerializer.Serialize(
                    new ThemeInstallation(installation.Source, manifest.Id!, checkout.Commit),
                    ThemeJsonContext.Default.ThemeInstallation
                )
            );
        }
        finally
        {
            if (Directory.Exists(temporary))
                RepositorySourceAcquirer.DeleteCheckout(temporary);
        }
    }

    private static void PublishDirectory(
        string staged,
        string destination,
        List<(string Destination, string Backup)> backups
    )
    {
        var backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(destination))
        {
            Directory.Move(destination, backup);
            backups.Add((destination, backup));
        }
        Directory.Move(staged, destination);
    }

    private static void PublishFile(
        string staged,
        string destination,
        List<(string Destination, string Backup)> backups
    )
    {
        var backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        if (File.Exists(destination))
        {
            File.Move(destination, backup);
            backups.Add((destination, backup));
        }
        File.Move(staged, destination);
    }

    private static void RestoreBackups(IEnumerable<(string Destination, string Backup)> backups)
    {
        foreach (var (destination, backup) in backups.Reverse())
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            else if (File.Exists(destination))
                File.Delete(destination);
            if (Directory.Exists(backup))
                Directory.Move(backup, destination);
            else if (File.Exists(backup))
                File.Move(backup, destination);
        }
    }

    private static ThemeManifest ReadManifest(string root)
    {
        var manifest =
            JsonSerializer.Deserialize(
                File.ReadAllText(Path.Combine(root, "theme.json")),
                ThemeJsonContext.Default.ThemeManifest
            ) ?? throw new InvalidDataException("Invalid theme manifest.");
        ThemeManifestValidator.Validate(manifest, root);
        return manifest;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed record StagedTheme(
        ThemeSource Source,
        string Root,
        ThemeManifest Manifest,
        string Commit,
        bool IsRoot
    );
}

public sealed record ThemeInstallation(ThemeSource Source, string Id, string Commit);
