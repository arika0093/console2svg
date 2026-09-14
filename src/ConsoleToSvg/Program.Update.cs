using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg;

internal static partial class Program
{
    private const string GitHubRepository = "arika0093/console2svg";
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();

    private static HttpClient CreateUpdateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("console2svg", ThisAssembly.AssemblyInformationalVersion)
        );
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        return client;
    }

    private static async Task<int> RunUpdateAsync(
        Cli.AppOptions options,
        CancellationToken cancellationToken
    )
    {
        var channel = await DetectInstallChannelAsync(cancellationToken).ConfigureAwait(false);
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var currentVersion = GetCurrentVersion();
        if (
            Version.TryParse(currentVersion, out var current)
            && Version.TryParse(release.Version, out var latest)
            && latest <= current
        )
        {
            await Console
                .Out.WriteLineAsync($"Already up to date ({currentVersion}).")
                .ConfigureAwait(false);
            return 0;
        }

        if (options.UpdateCheck)
        {
            await Console
                .Out.WriteLineAsync($"Update available: {currentVersion} -> {release.Version}")
                .ConfigureAwait(false);
            return 0;
        }

        if (channel.IsPackageManaged && !options.UpdateForce)
        {
            await Console
                .Error.WriteLineAsync($"This installation is managed by {channel.Name}.")
                .ConfigureAwait(false);
            await Console
                .Error.WriteLineAsync($"Use: {channel.UpdateCommand}")
                .ConfigureAwait(false);
            await Console
                .Error.WriteLineAsync("Use --force to replace the package-managed files directly.")
                .ConfigureAwait(false);
            return 1;
        }

        if (options.UpdateForce && channel.IsPackageManaged)
        {
            await Console
                .Error.WriteLineAsync(
                    $"Warning: replacing files owned by {channel.Name} will desynchronize its package database."
                )
                .ConfigureAwait(false);
        }

        if (!options.UpdateYes)
        {
            if (Console.IsInputRedirected)
            {
                await Console
                    .Error.WriteLineAsync(
                        "Confirmation is required. Re-run with --yes in a non-interactive environment."
                    )
                    .ConfigureAwait(false);
                return 1;
            }

            await Console
                .Out.WriteAsync(
                    $"Update console2svg from {currentVersion} to {release.Version}? [y/N] "
                )
                .ConfigureAwait(false);
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Out.WriteLineAsync("Update cancelled.").ConfigureAwait(false);
                return 0;
            }
        }

        var installDirectory = GetInstallDirectory();
        if (!CanWriteDirectory(installDirectory))
        {
            await Console
                .Error.WriteLineAsync($"Installation directory is not writable: {installDirectory}")
                .ConfigureAwait(false);
            return 1;
        }

        var asset = release.Assets.FirstOrDefault(item =>
            string.Equals(item.Name, GetArchiveName(), StringComparison.Ordinal)
        );
        if (asset is null)
        {
            await Console
                .Error.WriteLineAsync($"Release asset not found: {GetArchiveName()}")
                .ConfigureAwait(false);
            return 1;
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"console2svg-update-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archivePath = Path.Combine(tempDirectory, asset.Name);
            await DownloadFileAsync(asset.DownloadUrl, archivePath, cancellationToken)
                .ConfigureAwait(false);
            await VerifyDigestAsync(asset, archivePath, cancellationToken).ConfigureAwait(false);

            var extractedDirectory = Path.Combine(tempDirectory, "extracted");
            Directory.CreateDirectory(extractedDirectory);
            await ExtractArchiveAsync(archivePath, extractedDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (OperatingSystem.IsWindows())
            {
                StartWindowsReplacement(extractedDirectory, installDirectory, tempDirectory);
                await Console
                    .Out.WriteLineAsync(
                        "Update scheduled. It will be applied after this process exits."
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                ReplaceFiles(extractedDirectory, installDirectory);
                await Console
                    .Out.WriteLineAsync($"Updated console2svg to {release.Version}.")
                    .ConfigureAwait(false);
                Directory.Delete(tempDirectory, recursive: true);
            }
            return 0;
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or Win32Exception
                        or InvalidOperationException
            )
        {
            await Console
                .Error.WriteLineAsync($"Update failed: {ex.Message}")
                .ConfigureAwait(false);
            TryDeleteDirectory(tempDirectory);
            return 1;
        }
    }

    private static async Task<InstallChannelInfo> DetectInstallChannelAsync(
        CancellationToken cancellationToken
    )
    {
        if (
            string.Equals(
                Environment.GetEnvironmentVariable("CONSOLE2SVG_INSTALL_CHANNEL"),
                "npm",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return new("npm", "npm update -g console2svg", true);

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            return InstallChannelInfo.Standalone;

        var resolvedExecutablePath = ResolveExecutablePath(executablePath);
        if (IsNpmManagedPath(resolvedExecutablePath))
            return new("npm", "npm update -g console2svg", true);

        if (OperatingSystem.IsLinux())
        {
            if (
                await IsOwnedByPackageAsync(
                    "dpkg-query",
                    "-S",
                    resolvedExecutablePath,
                    cancellationToken
                )
            )
                return new(
                    "deb",
                    "sudo apt update && sudo apt install --only-upgrade console2svg",
                    true
                );
            if (
                await IsOwnedByPackageAsync("rpm", "-qf", resolvedExecutablePath, cancellationToken)
            )
                return new("rpm", "sudo dnf upgrade console2svg", true);
        }

        if (
            OperatingSystem.IsWindows()
            && await IsWingetInstallationAsync(resolvedExecutablePath, cancellationToken)
        )
            return new("winget", "winget upgrade --id arika0093.console2svg --exact", true);

        return InstallChannelInfo.Standalone;
    }

    private static bool IsNpmManagedPath(string resolvedExecutablePath) =>
        resolvedExecutablePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase)
        && resolvedExecutablePath.Contains("console2svg", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsOwnedByPackageAsync(
        string command,
        string argument,
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var executable = FindExecutableInPath(command);
            if (executable is null)
                return false;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.ArgumentList.Add(path);
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> IsWingetInstallationAsync(
        string resolvedExecutablePath,
        CancellationToken cancellationToken
    )
    {
        // 'winget list' only proves the package exists somewhere on this machine.
        // When standalone and winget copies coexist, the running standalone binary
        // must not be reported as winget-managed, so require the current
        // executable to live under a winget-owned location first.
        if (!IsLikelyWingetPath(resolvedExecutablePath))
            return false;

        try
        {
            var executable = FindExecutableInPath("winget");
            if (executable is null)
                return false;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add("list");
            process.StartInfo.ArgumentList.Add("--id");
            process.StartInfo.ArgumentList.Add("arika0093.console2svg");
            process.StartInfo.ArgumentList.Add("--exact");
            process.StartInfo.ArgumentList.Add("--accept-source-agreements");
            process.Start();
            var output = await process
                .StandardOutput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                && output.Contains("arika0093.console2svg", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsLikelyWingetPath(string resolvedExecutablePath)
    {
        return resolvedExecutablePath.Contains(
                Path.Combine("Microsoft", "WinGet", "Packages"),
                StringComparison.OrdinalIgnoreCase
            )
            || resolvedExecutablePath.Contains(
                $"{Path.DirectorySeparatorChar}WinGet{Path.DirectorySeparatorChar}Packages{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase
            )
            || resolvedExecutablePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ReleaseInfo> GetLatestReleaseAsync(
        CancellationToken cancellationToken
    )
    {
        using var response = await UpdateHttpClient
            .GetAsync(
                $"https://api.github.com/repos/{GitHubRepository}/releases/latest",
                cancellationToken
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
        );
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = tag.TrimStart('v');
        var assets = root.GetProperty("assets")
            .EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null
            ))
            .ToArray();
        return new ReleaseInfo(version, assets);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        CancellationToken cancellationToken
    )
    {
        using var response = await UpdateHttpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyDigestAsync(
        ReleaseAsset asset,
        string archivePath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(asset.Digest))
            return;
        var expected = asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? asset.Digest["sha256:".Length..]
            : asset.Digest;
        await using var stream = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)
        );
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 verification failed for {asset.Name}.");
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken
    )
    {
        if (OperatingSystem.IsWindows())
        {
            await ZipFile
                .ExtractToDirectoryAsync(archivePath, destination, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var tar = FindExecutableInPath("tar");
        if (tar is null)
            throw new InvalidDataException("The tar command is required to extract updates.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(tar)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-xzf");
        process.StartInfo.ArgumentList.Add(archivePath);
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(destination);
        process.Start();
        var error = await process
            .StandardError.ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Archive extraction failed: {error.Trim()}");
    }

    private static void ReplaceFiles(string sourceDirectory, string targetDirectory)
    {
        foreach (
            var source in Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(sourceDirectory, source);
            var target = Path.Combine(targetDirectory, relative);
            var fullTarget = Path.GetFullPath(target);
            var targetRoot =
                Path.GetFullPath(targetDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullTarget.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Archive entry escapes the installation directory: {relative}"
                );
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            if (!OperatingSystem.IsWindows() && Path.GetFileName(source) == "console2svg")
                File.SetUnixFileMode(
                    target,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute
                );
        }
    }

    private static void StartWindowsReplacement(
        string sourceDirectory,
        string targetDirectory,
        string temporaryDirectory
    )
    {
        static string Quote(string value) =>
            "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var script =
            $"$p={Environment.ProcessId}; while (Get-Process -Id $p -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 200 }}; "
            + $"Get-ChildItem -LiteralPath {Quote(sourceDirectory)} | Copy-Item -Destination {Quote(targetDirectory)} -Recurse -Force; "
            + $"Remove-Item -LiteralPath {Quote(temporaryDirectory)} -Recurse -Force";
        var powershell = FindExecutableInPath("powershell");
        if (powershell is null)
            throw new InvalidOperationException("PowerShell is required to update on Windows.");
        Process.Start(
            new ProcessStartInfo(powershell)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            }
        );
    }

    private static string GetInstallDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var resolved = ResolveExecutablePath(processPath);
            var resolvedDirectory = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory))
                return resolvedDirectory;
        }
        return AppContext.BaseDirectory;
    }

    private static string ResolveExecutablePath(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? Path.GetFullPath(path);
        }
        catch (IOException ex)
        {
            _ = ex;
            return Path.GetFullPath(path);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".console2svg-write-{Guid.NewGuid():N}");
            File.Create(probe).Dispose();
            File.Delete(probe);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            _ = ex;
        }
        catch (UnauthorizedAccessException ex)
        {
            _ = ex;
        }
    }

    private static string GetArchiveName()
    {
        string rid;
        if (OperatingSystem.IsWindows())
            rid = $"win-{GetArchitectureSuffix()}.zip";
        else if (OperatingSystem.IsMacOS())
            rid = $"osx-{GetArchitectureSuffix()}.tar.gz";
        else
            rid = $"linux-{GetArchitectureSuffix()}.tar.gz";
        return $"console2svg-{rid}";
    }

    private static string GetArchitectureSuffix() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}"
            ),
        };

    private static string GetCurrentVersion() =>
        ThisAssembly.AssemblyInformationalVersion.Split('+')[0];

    private sealed record ReleaseInfo(string Version, ReleaseAsset[] Assets);

    private sealed record ReleaseAsset(string Name, string DownloadUrl, string? Digest);

    private sealed record InstallChannelInfo(
        string Name,
        string UpdateCommand,
        bool IsPackageManaged
    )
    {
        public static InstallChannelInfo Standalone { get; } = new("standalone", "", false);
    }
}
