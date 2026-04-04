using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ConsoleToSvg.Conversion;

internal static class ExternalToolRunner
{
    public static async Task RunAsync(
        string executablePath,
        IEnumerable<string> arguments,
        ILogger logger,
        CancellationToken cancellationToken,
        string? workingDirectory = null
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        logger.LogDebug(
            "Running external tool: {FileName} {Arguments}",
            process.StartInfo.FileName,
            string.Join(' ', process.StartInfo.ArgumentList)
        );

        process.Start();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup failures on cancellation.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            logger.LogDebug("External tool stdout: {Stdout}", stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            logger.LogDebug("External tool stderr: {Stderr}", stderr.Trim());
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{System.IO.Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}".TrimEnd()
            );
        }
    }
}
