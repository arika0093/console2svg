using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Batch;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static async Task<int> RunBatchAsync(AppOptions options, CancellationToken ct)
    {
        var inputDir = Path.GetFullPath(options.BatchInputDir ?? "docs");
        var assetsDir = Path.GetFullPath(options.BatchAssetsDir ?? "assets");
        if (!Directory.Exists(inputDir))
        {
            await Console.Error.WriteLineAsync(
                $"Input directory not found: {inputDir}".AsMemory(),
                ct
            );
            return 1;
        }

        Directory.CreateDirectory(assetsDir);

        var files = Directory
            .EnumerateFiles(inputDir, "*.md", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(inputDir, "*.mdx", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            await Console.Error.WriteLineAsync(
                $"No markdown files found in {inputDir}".AsMemory(),
                ct
            );
            return 0;
        }

        using var loggerFactory = CreateLoggerFactory(options.Verbose, options.VerboseLogPath);
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.Batch");
        var failures = new List<string>();
        var generated = 0;
        var cached = 0;
        var lockObject = new object();

        void Fail(string message)
        {
            lock (lockObject)
            {
                failures.Add(message);
            }
        }

        await Parallel
            .ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                    CancellationToken = ct,
                },
                async (file, fileCt) =>
                {
                    try
                    {
                        var counts = await RunBatchFileAsync(
                                file,
                                inputDir,
                                assetsDir,
                                options,
                                loggerFactory,
                                logger,
                                Fail,
                                fileCt
                            )
                            .ConfigureAwait(false);
                        lock (lockObject)
                        {
                            generated += counts.Generated;
                            cached += counts.Cached;
                        }
                    }
                    catch (OperationCanceledException) when (fileCt.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.ZLogDebug(ex, $"Batch failed for {file}");
                        Fail($"{file}: {ex.Message}");
                    }
                }
            )
            .ConfigureAwait(false);

        foreach (var failure in failures.OrderBy(m => m, StringComparer.Ordinal))
        {
            await Console.Error.WriteLineAsync($"error: {failure}".AsMemory(), ct);
        }

        await Console.Error.WriteLineAsync(
            $"Batch done: {generated} generated, {cached} cached, {failures.Count} failed.".AsMemory(),
            ct
        );
        return failures.Count > 0 ? 1 : 0;
    }

    private sealed record BatchFileCounts(int Generated, int Cached);

    private static async Task<BatchFileCounts> RunBatchFileAsync(
        string mdPath,
        string inputDir,
        string assetsDir,
        AppOptions defaults,
        ILoggerFactory loggerFactory,
        ILogger logger,
        Action<string> fail,
        CancellationToken ct
    )
    {
        var original = await File.ReadAllTextAsync(mdPath, ct).ConfigureAwait(false);
        var parsed = BatchMarkdown.Parse(original, mdPath);
        foreach (var error in parsed.Errors)
        {
            fail($"{RelativeTo(mdPath, inputDir)}:{error.Line}: {error.Message}");
        }

        if (parsed.Errors.Count > 0 || parsed.Jobs.Count == 0)
        {
            return new BatchFileCounts(0, 0);
        }

        if (defaults.BatchDry)
        {
            foreach (var job in parsed.Jobs)
            {
                var outputAbs = BatchExecutor.ResolveOutput(assetsDir, job.OutputRelative);
                await Console
                    .Error.WriteLineAsync(
                        $"[dry] {RelativeTo(mdPath, inputDir)}:{job.MarkerLine} -> {outputAbs} : {job.Alt}".AsMemory(),
                        ct
                    )
                    .ConfigureAwait(false);
            }

            return new BatchFileCounts(0, 0);
        }

        var links = new List<BatchLink>();
        var generated = 0;
        var cached = 0;
        foreach (var job in parsed.Jobs)
        {
            ct.ThrowIfCancellationRequested();
            var outputAbs = BatchExecutor.ResolveOutput(assetsDir, job.OutputRelative);
            if (outputAbs is null)
            {
                fail(
                    $"{RelativeTo(mdPath, inputDir)}:{job.MarkerLine}: output escapes assets dir."
                );
                continue;
            }

            var jobOptions = BuildBatchJobOptions(defaults, job);
            var fingerprint = BuildBatchFingerprint(jobOptions);
            var hash = BatchMarkdown.ComputeJobHash(
                job.Setup,
                job.Capture,
                job.Teardown,
                fingerprint,
                ThisAssembly.AssemblyInformationalVersion
            );
            var sidecar = outputAbs + ".sha256";
            if (
                defaults.BatchCached
                && await BatchExecutor.IsCacheHitAsync(outputAbs, hash, ct).ConfigureAwait(false)
            )
            {
                links.Add(new BatchLink(job, BatchExecutor.Relativize(mdPath, outputAbs)));
                cached++;
                await Console
                    .Error.WriteLineAsync($"Cached: {outputAbs}".AsMemory(), ct)
                    .ConfigureAwait(false);
                continue;
            }

            var result = await ExecuteBatchJobAsync(
                    job,
                    jobOptions,
                    outputAbs,
                    loggerFactory,
                    logger,
                    ct
                )
                .ConfigureAwait(false);
            if (result is not null)
            {
                fail($"{RelativeTo(mdPath, inputDir)}:{job.MarkerLine}: {result}");
                continue;
            }

            await File.WriteAllTextAsync(sidecar, hash + "\n", Encoding.ASCII, ct)
                .ConfigureAwait(false);
            links.Add(new BatchLink(job, BatchExecutor.Relativize(mdPath, outputAbs)));
            generated++;
            await Console
                .Error.WriteLineAsync($"Generated: {outputAbs}".AsMemory(), ct)
                .ConfigureAwait(false);
        }

        if (links.Count > 0)
        {
            var rewritten = BatchMarkdown.RewriteLinks(original, links);
            if (!string.Equals(rewritten, original, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(mdPath, rewritten, DetectBatchEncoding(mdPath), ct)
                    .ConfigureAwait(false);
            }
        }

        return new BatchFileCounts(generated, cached);
    }

    private static AppOptions BuildBatchJobOptions(AppOptions defaults, BatchParsedJob job)
    {
        var jobOptions = defaults.ShallowClone();
        if (job.Width.HasValue)
        {
            jobOptions.Width = job.Width;
            jobOptions.WidthAdjust = false;
        }

        if (job.Height.HasValue)
        {
            jobOptions.Height = job.Height;
            jobOptions.HeightAdjust = false;
        }

        if (job.WithCommand)
        {
            jobOptions.WithCommand = true;
        }

        if (job.WindowExplicit)
        {
            jobOptions.Window = job.Window ?? "macos";
            jobOptions.IsWindowExplicit = true;
        }

        if (job.Video)
        {
            jobOptions.Mode = OutputMode.Video;
            jobOptions.IsModeExplicit = true;
        }

        if (job.Timeout.HasValue)
        {
            jobOptions.Timeout = job.Timeout;
        }

        jobOptions.Command = FirstBatchLine(job.Capture);
        if (string.IsNullOrWhiteSpace(jobOptions.Prompt))
        {
            jobOptions.Prompt = GetDefaultPrompt();
        }

        return jobOptions;
    }

    private static string BuildBatchFingerprint(AppOptions jobOptions) =>
        $"w={jobOptions.Width}/{jobOptions.WidthAdjust};h={jobOptions.Height}/{jobOptions.HeightAdjust};"
        + $"c={jobOptions.WithCommand};d={jobOptions.Window}/{jobOptions.IsWindowExplicit};"
        + $"m={jobOptions.Mode};t={jobOptions.Theme};timeout={jobOptions.Timeout}";

    /// <summary>
    /// Returns null on success, otherwise an error message.
    /// Setup runs silently in the same shell; only capture is recorded.
    /// </summary>
    private static async Task<string?> ExecuteBatchJobAsync(
        BatchParsedJob job,
        AppOptions jobOptions,
        string outputAbs,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken ct
    )
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var setupLog =
            job.Setup.Length > 0
                ? Path.Combine(Path.GetTempPath(), $"c2s-batch-{Guid.NewGuid():N}.log")
                : null;
        var script = BatchExecutor.BuildScript(job, setupLog, isWindows);
        var width = ResolveSize(
            jobOptions.Width,
            jobOptions.WidthAdjust,
            TryGetConsoleWidth,
            DefaultWidth
        );
        var height = ResolveSize(
            jobOptions.Height,
            jobOptions.HeightAdjust,
            TryGetConsoleHeight,
            DefaultHeight
        );

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (jobOptions.Timeout.HasValue)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(jobOptions.Timeout.Value));
        }

        RecordingSession session;
        try
        {
            using (ApplyProcessEnvironmentOverrides(jobOptions, logger))
            {
                session = await PtyRecorder
                    .RecordAsync(
                        script,
                        width,
                        height,
                        timeoutCts.Token,
                        loggerFactory.CreateLogger("ConsoleToSvg.BatchPty"),
                        forwardToConsole: false,
                        noDeleteEnvs: jobOptions.NoDeleteEnvs,
                        outputCoalesceMs: jobOptions.Mode == OutputMode.Video ? null : 0d,
                        videoFps: jobOptions.VideoFps
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"timed out after {jobOptions.Timeout}s.";
        }

        if (!BatchExecutor.TryTrimBeforeMarker(session))
        {
            var hint =
                setupLog is not null && File.Exists(setupLog)
                    ? " Setup log: "
                        + await File.ReadAllTextAsync(setupLog, ct).ConfigureAwait(false)
                    : string.Empty;
            CleanupTempFile(setupLog, logger);
            return "setup failed before capture started." + hint;
        }

        CleanupTempFile(setupLog, logger);

        try
        {
            var renderOptions = SvgRenderOptionsFactory.Create(jobOptions);
            EnsureDirectory(outputAbs);
            if (jobOptions.Mode is OutputMode.Video)
            {
                await using var writer = new StreamWriter(
                    outputAbs,
                    append: false,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                );
                AnimatedSvgRenderer.Write(writer, session, renderOptions);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var svg = SvgRenderer.Render(session, renderOptions);
                await File.WriteAllTextAsync(
                        outputAbs,
                        svg,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        ct
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Batch render failed for {outputAbs}");
            return $"render failed: {ex.Message}";
        }

        if (job.Teardown.Length > 0)
        {
            var (exitCode, output) = await RunShellScriptAsync(job.Teardown, ct)
                .ConfigureAwait(false);
            if (exitCode != 0)
            {
                return $"teardown exited with code {exitCode}: {output.Trim()}";
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string Output)> RunShellScriptAsync(
        string script,
        CancellationToken ct
    )
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe"
            );
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(script);
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (1, "failed to start shell.");
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return (process.ExitCode, output + error);
    }

    private static string RelativeTo(string path, string baseDir) =>
        Path.GetRelativePath(baseDir, Path.GetFullPath(path))
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string FirstBatchLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static Encoding DetectBatchEncoding(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length >= 3)
            {
                var preamble = new byte[3];
                _ = stream.Read(preamble, 0, 3);
                if (preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF)
                {
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                }
            }
        }
        catch
        {
            // Fall through to UTF-8 without BOM.
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static void CleanupTempFile(string? path, ILogger logger)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Failed to delete temp file {path}.");
        }
    }
}
