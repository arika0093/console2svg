using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ConsoleToSvg.Tool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!TryGetRuntimeAsset(out var rid, out var executableName))
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
                    CopyBundledFiles(rid, executableName, distributionDirectory);
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

        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) => eventArgs.Cancel = true;
        try
        {
            if (!process.Start())
            {
                await Console.Error.WriteLineAsync($"console2svg: failed to start '{executablePath}'.");
                return 1;
            }

            Console.CancelKeyPress += cancelKeyPressHandler;
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            finally
            {
                Console.CancelKeyPress -= cancelKeyPressHandler;
            }

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
        out string executableName
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
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            rid = $"win-{architecture}";
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            if (IsMuslLinux())
            {
                rid = string.Empty;
                return false;
            }

            rid = $"linux-{architecture}";
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            rid = $"osx-{architecture}";
            return true;
        }

        rid = string.Empty;
        return false;
    }

    private static void CopyBundledFiles(string rid, string executableName, string distributionDirectory)
    {
        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "native", rid);
        if (!Directory.Exists(bundledDirectory))
        {
            throw new InvalidOperationException($"The package does not contain native assets for '{rid}'.");
        }

        var stagingDirectory = Path.Combine(
            distributionDirectory,
            $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}"
        );
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var sourcePath in Directory.GetFiles(bundledDirectory, "*", SearchOption.AllDirectories))
            {
                var destinationPath = Path.Combine(
                    stagingDirectory,
                    Path.GetRelativePath(bundledDirectory, sourcePath)
                );
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }

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
        return ThisAssembly.NuGetPackageVersion;
    }

    private static string GetDistributionDirectory()
    {
        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheRoot = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : !string.IsNullOrWhiteSpace(xdgCacheHome) && Path.IsPathFullyQualified(xdgCacheHome)
                ? xdgCacheHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(cacheRoot, "console2svg", GetReleaseVersion());
    }

    private static async Task<FileStream> AcquireInstallationLockAsync(string distributionDirectory)
    {
        var lockPath = Path.Combine(distributionDirectory, ".install.lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Timed out waiting for installation lock at '{lockPath}'.",
                    ex
                );
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
