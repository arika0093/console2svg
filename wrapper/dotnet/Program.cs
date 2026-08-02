using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ConsoleToSvg.Tool;

internal static class Program
{
    private const string ReleaseBaseUrl = "https://github.com/arika0093/console2svg/releases/download";

    public static async Task<int> Main(string[] args)
    {
        if (!TryGetRuntimeAsset(out var rid, out var executableName, out var archiveExtension))
        {
            await Console.Error.WriteLineAsync(
                $"console2svg: unsupported platform: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}."
            );
            return 1;
        }

        var distributionDirectory = GetDistributionDirectory();
        var executablePath = Path.Combine(distributionDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            try
            {
                Directory.CreateDirectory(distributionDirectory);
                await using var installationLock = await AcquireInstallationLockAsync(distributionDirectory)
                    .ConfigureAwait(false);
                if (!File.Exists(executablePath))
                {
                    await DownloadAndExtractAsync(
                            rid,
                            executableName,
                            archiveExtension,
                            distributionDirectory
                        )
                        .ConfigureAwait(false);
                    if (!OperatingSystem.IsWindows())
                    {
                        await MakeExecutableAsync(executablePath).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"console2svg: failed to install the native binary: {ex.Message}");
                return 1;
            }
        }

        if (!File.Exists(executablePath))
        {
            await Console.Error.WriteLineAsync(
                $"console2svg: the native binary was not found at '{executablePath}' after installation."
            );
            return 1;
        }

        using var process = new Process
        {
            StartInfo =
            {
                FileName = executablePath,
                UseShellExecute = false,
            },
        };
        foreach (var argument in args)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                await Console.Error.WriteLineAsync($"console2svg: failed to start '{executablePath}'.");
                return 1;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"console2svg: failed to start '{executablePath}': {ex.Message}"
            );
            return 1;
        }
    }

    private static bool TryGetRuntimeAsset(
        out string rid,
        out string executableName,
        out string archiveExtension
    )
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };

        executableName = OperatingSystem.IsWindows() ? "console2svg.exe" : "console2svg";
        if (string.IsNullOrEmpty(architecture))
        {
            rid = string.Empty;
            archiveExtension = string.Empty;
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            rid = $"win-{architecture}";
            archiveExtension = "zip";
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            if (IsMuslLinux())
            {
                rid = string.Empty;
                archiveExtension = string.Empty;
                return false;
            }

            rid = $"linux-{architecture}";
            archiveExtension = "tar.gz";
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            rid = $"osx-{architecture}";
            archiveExtension = "tar.gz";
            return true;
        }

        rid = string.Empty;
        archiveExtension = string.Empty;
        return false;
    }

    private static async Task DownloadAndExtractAsync(
        string rid,
        string executableName,
        string archiveExtension,
        string distributionDirectory
    )
    {
        var version = GetReleaseVersion();
        var archiveName = $"console2svg-{rid}.{archiveExtension}";
        var stagingDirectory = Path.Combine(
            distributionDirectory,
            $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}"
        );
        var archivePath = Path.Combine(stagingDirectory, archiveName);
        var downloadUrl = $"{ReleaseBaseUrl}/v{version}/{archiveName}";

        await Console.Error.WriteLineAsync($"console2svg: downloading {downloadUrl}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("console2svg-dotnet-tool-wrapper");
            await using var input = await client
                .GetStreamAsync(downloadUrl)
                .ConfigureAwait(false);
            await using (var output = File.Create(archivePath))
            {
                await input.CopyToAsync(output).ConfigureAwait(false);
            }

            if (OperatingSystem.IsWindows())
            {
                ZipFile.ExtractToDirectory(archivePath, stagingDirectory);
            }
            else
            {
                using var tar = Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "tar",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        ArgumentList = { "-xzf", archivePath, "-C", stagingDirectory },
                    }
                ) ?? throw new InvalidOperationException("Unable to start tar.");
                var standardError = await tar.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await tar.WaitForExitAsync().ConfigureAwait(false);
                if (tar.ExitCode != 0)
                {
                    throw new InvalidOperationException($"tar failed: {standardError.Trim()}");
                }
            }

            File.Delete(archivePath);
            PublishStagedFiles(stagingDirectory, distributionDirectory, executableName);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    private static string GetReleaseVersion()
    {
        var version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "ConsoleToSvgReleaseVersion")
            ?.Value;
        return version ?? throw new InvalidOperationException("Tool release version is unavailable.");
    }

    private static string GetDistributionDirectory()
    {
        var cacheRoot = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(cacheRoot, "console2svg", GetReleaseVersion());
    }

    private static async Task<FileStream> AcquireInstallationLockAsync(string distributionDirectory)
    {
        var lockPath = Path.Combine(distributionDirectory, ".install.lock");
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }
    }

    private static void PublishStagedFiles(
        string stagingDirectory,
        string distributionDirectory,
        string executableName
    )
    {
        var stagedExecutablePath = Path.Combine(stagingDirectory, executableName);
        if (!File.Exists(stagedExecutablePath))
        {
            throw new InvalidOperationException("The downloaded archive does not contain the native executable.");
        }

        foreach (var stagedFile in Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(stagedFile, stagedExecutablePath, StringComparison.Ordinal))
            {
                continue;
            }

            var destinationPath = Path.Combine(
                distributionDirectory,
                Path.GetRelativePath(stagingDirectory, stagedFile)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(stagedFile, destinationPath, true);
        }

        File.Move(stagedExecutablePath, Path.Combine(distributionDirectory, executableName), true);
    }

    private static bool IsMuslLinux()
    {
        if (File.Exists("/etc/alpine-release"))
        {
            return true;
        }

        try
        {
            using var ldd = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "ldd",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList = { "--version" },
                }
            );
            if (ldd is null)
            {
                return false;
            }

            var output = ldd.StandardOutput.ReadToEnd() + ldd.StandardError.ReadToEnd();
            ldd.WaitForExit();
            return output.Contains("musl", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task MakeExecutableAsync(string executablePath)
    {
        using var chmod = Process.Start(
            new ProcessStartInfo
            {
                FileName = "chmod",
                UseShellExecute = false,
                ArgumentList = { "+x", executablePath },
            }
        ) ?? throw new InvalidOperationException("Unable to start chmod.");
        await chmod.WaitForExitAsync().ConfigureAwait(false);
        if (chmod.ExitCode != 0)
        {
            throw new InvalidOperationException("chmod failed.");
        }
    }
}
