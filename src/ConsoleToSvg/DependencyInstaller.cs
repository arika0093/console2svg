using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ConsoleToSvg;

internal static class DependencyInstaller
{
    [SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S1075:URIs should not be hardcoded",
        Justification = "This is the upstream URL also used by the release packaging script."
    )]
    private const string FfmpegReleaseBaseUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

    public static async Task InstallFfmpegAsync()
    {
        var package = GetFfmpegPackage();
        var applicationDirectory = AppContext.BaseDirectory;
        var targetDirectory = Path.Combine(applicationDirectory, "ffmpeg");
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"console2svg-ffmpeg-{Guid.NewGuid():N}"
        );
        // Directory.Move requires source and destination to be on the same filesystem.
        // Keep the final staging directory beside the destination because /tmp can be
        // mounted separately from a dotnet tool installation.
        var stagedDirectory = Path.Combine(
            applicationDirectory,
            $".console2svg-ffmpeg-{Guid.NewGuid():N}"
        );

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var archivePath = Path.Combine(temporaryDirectory, package.ArchiveName);
            Console.WriteLine($"Downloading ffmpeg from {package.Url}");

            using var client = new HttpClient();
            await using (
                var archive = new FileStream(
                    archivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            using (
                var response = await client
                    .GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false)
            )
            {
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(archive).ConfigureAwait(false);
            }

            var extractDirectory = Path.Combine(temporaryDirectory, "extract");
            if (package.IsZip)
            {
#if NET10_0_OR_GREATER
                await ZipFile
                    .ExtractToDirectoryAsync(archivePath, extractDirectory)
                    .ConfigureAwait(false);
#else
                ZipFile.ExtractToDirectory(archivePath, extractDirectory);
#endif
            }
            else
            {
                await ExtractTarArchiveAsync(archivePath, extractDirectory).ConfigureAwait(false);
            }

            var binaryPath = FindFfmpegBinary(extractDirectory, package.ExecutableName);
            var sourceDirectory = Path.GetDirectoryName(binaryPath)
                ?? throw new InvalidOperationException("The downloaded ffmpeg archive has no bin directory.");
            CopyDirectory(sourceDirectory, stagedDirectory);

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
            Directory.Move(stagedDirectory, targetDirectory);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(
                    Path.Combine(targetDirectory, package.ExecutableName),
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute
                );
            }

            Console.WriteLine($"Installed ffmpeg to {targetDirectory}");
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            if (Directory.Exists(stagedDirectory))
            {
                Directory.Delete(stagedDirectory, recursive: true);
            }
        }
    }

    private static FfmpegPackage GetFfmpegPackage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => CreatePackage("ffmpeg-master-latest-win64-lgpl-shared.zip", true),
                Architecture.Arm64 => CreatePackage("ffmpeg-master-latest-winarm64-lgpl-shared.zip", true),
                _ => throw new PlatformNotSupportedException(
                    $"--install-deps does not support Windows {RuntimeInformation.ProcessArchitecture}."
                ),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => CreatePackage("ffmpeg-master-latest-linux64-lgpl.tar.xz", false),
                Architecture.Arm64 => CreatePackage("ffmpeg-master-latest-linuxarm64-lgpl.tar.xz", false),
                _ => throw new PlatformNotSupportedException(
                    $"--install-deps does not support Linux {RuntimeInformation.ProcessArchitecture}."
                ),
            };
        }

        throw new PlatformNotSupportedException(
            "--install-deps supports Windows and Linux. On macOS, install ffmpeg with 'brew install ffmpeg'."
        );
    }

    private static FfmpegPackage CreatePackage(string archiveName, bool isZip) =>
        new(
            FfmpegReleaseBaseUrl + archiveName,
            archiveName,
            isZip,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg"
        );

    private static async Task ExtractTarArchiveAsync(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/usr/bin/tar")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-xf");
        process.StartInfo.ArgumentList.Add(archivePath);
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(destinationDirectory);

        process.Start();
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to extract the downloaded ffmpeg archive: {error}");
        }
    }

    private static string FindFfmpegBinary(string directory, string executableName)
    {
        var binaryPath = Directory
            .EnumerateFiles(directory, executableName, SearchOption.AllDirectories)
            .FirstOrDefault(file =>
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(file)),
                    "bin",
                    StringComparison.Ordinal
                )
            );
        if (binaryPath is not null)
        {
            return binaryPath;
        }

        throw new InvalidOperationException(
            $"The downloaded ffmpeg archive does not contain bin/{executableName}."
        );
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        }
    }

    private sealed record FfmpegPackage(
        string Url,
        string ArchiveName,
        bool IsZip,
        string ExecutableName
    );
}
