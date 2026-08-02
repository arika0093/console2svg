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

        var distributionDirectory = Path.Combine(AppContext.BaseDirectory, "dist");
        var executablePath = Path.Combine(distributionDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            try
            {
                await DownloadAndExtractAsync(rid, archiveExtension, distributionDirectory).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"console2svg: failed to install the native binary: {ex.Message}");
                return 1;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            await MakeExecutableAsync(executablePath).ConfigureAwait(false);
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

        process.Start();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
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
        string archiveExtension,
        string distributionDirectory
    )
    {
        Directory.CreateDirectory(distributionDirectory);
        var version = GetReleaseVersion();
        var archiveName = $"console2svg-{rid}.{archiveExtension}";
        var archivePath = Path.Combine(distributionDirectory, $".{archiveName}.tmp");
        var downloadUrl = $"{ReleaseBaseUrl}/v{version}/{archiveName}";

        await Console.Error.WriteLineAsync($"console2svg: downloading {downloadUrl}");
        try
        {
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
                ZipFile.ExtractToDirectory(archivePath, distributionDirectory, true);
                return;
            }

            using var tar = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "tar",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    ArgumentList = { "-xzf", archivePath, "-C", distributionDirectory },
                }
            ) ?? throw new InvalidOperationException("Unable to start tar.");
            var standardError = await tar.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await tar.WaitForExitAsync().ConfigureAwait(false);
            if (tar.ExitCode != 0)
            {
                throw new InvalidOperationException($"tar failed: {standardError.Trim()}");
            }
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    private static string GetReleaseVersion()
    {
        var version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return (version ?? throw new InvalidOperationException("Tool version is unavailable."))
            .Split('+', 2)[0];
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
