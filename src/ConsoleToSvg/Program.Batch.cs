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
    private sealed record BatchResolvedJob(BatchParsedJob Job, string OutputPath);

    private sealed record BatchFilePlan(
        string Path,
        string RelativePath,
        string Original,
        BatchParseResult Parsed,
        List<BatchResolvedJob> Jobs
    );

    private static async Task<int> RunBatchAsync(AppOptions options, CancellationToken ct)
    {
        var inputPath = Path.GetFullPath(options.BatchInputPath ?? "docs");
        var outputDir = Path.GetFullPath(options.BatchOutputDir ?? "assets");
        if (!TryFindBatchFiles(inputPath, out var inputRoot, out var files, out var inputError))
        {
            await Console.Error.WriteLineAsync(inputError.AsMemory(), ct);
            return 1;
        }

        var filters = options.BatchFilters.Select(BatchExecutor.NormalizeFilter).ToArray();
        var selected = files
            .Select(path => new
            {
                Path = path,
                Relative = BatchExecutor.NormalizePath(Path.GetRelativePath(inputRoot, path)),
            })
            .Where(file =>
                filters.Length == 0
                || filters.Any(filter => BatchExecutor.MatchesFilter(file.Relative, filter))
            )
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToArray();

        if (selected.Length == 0)
        {
            var message =
                filters.Length == 0
                    ? $"No Markdown files found in {inputPath}"
                    : $"No Markdown files matched the requested filters under {inputPath}";
            await Console.Error.WriteLineAsync(message.AsMemory(), ct);
            return 0;
        }

        var plans = new List<BatchFilePlan>(selected.Length);
        var failures = new List<string>();
        foreach (var file in selected)
        {
            var original = await File.ReadAllTextAsync(file.Path, ct).ConfigureAwait(false);
            var parsed = BatchMarkdown.Parse(original, file.Path);
            foreach (var error in parsed.Errors)
            {
                failures.Add($"{file.Relative}:{error.Line}: {error.Message}");
            }

            plans.Add(new BatchFilePlan(file.Path, file.Relative, original, parsed, []));
        }

        if (failures.Count == 0)
        {
            ResolveBatchOutputs(plans, inputRoot, outputDir, failures);
        }

        if (failures.Count == 0 && filters.Length > 0)
        {
            await ValidateFilteredSharedOutputsAsync(
                    files,
                    selected.Select(file => file.Path),
                    plans,
                    inputRoot,
                    outputDir,
                    failures,
                    ct
                )
                .ConfigureAwait(false);
        }

        if (failures.Count > 0)
        {
            await WriteBatchFailuresAsync(failures, ct).ConfigureAwait(false);
            return 1;
        }

        if (options.BatchDryRun)
        {
            foreach (var plan in plans)
            {
                foreach (var resolved in plan.Jobs)
                {
                    await Console.Error.WriteLineAsync(
                        $"[dry-run] {plan.RelativePath}:{resolved.Job.MarkerLine} -> {resolved.OutputPath} : {resolved.Job.Alt}".AsMemory(),
                        ct
                    );
                }
            }

            await Console.Error.WriteLineAsync(
                $"Batch markdown dry run: {plans.Sum(plan => plan.Jobs.Count)} job(s).".AsMemory(),
                ct
            );
            return 0;
        }

        using var loggerFactory = CreateLoggerFactory(options.Verbose, options.VerboseLogPath);
        var logger = loggerFactory.CreateLogger("ConsoleToSvg.BatchMarkdown");
        var generated = 0;

        foreach (var plan in plans)
        {
            var links = new List<BatchLink>();
            foreach (var resolved in plan.Jobs)
            {
                ct.ThrowIfCancellationRequested();
                var jobOptions = BuildBatchJobOptions(options, resolved.Job);
                var error = await ExecuteBatchJobAsync(
                        resolved.Job,
                        jobOptions,
                        resolved.OutputPath,
                        loggerFactory,
                        logger,
                        ct
                    )
                    .ConfigureAwait(false);
                if (error is not null)
                {
                    failures.Add($"{plan.RelativePath}:{resolved.Job.MarkerLine}: {error}");
                    continue;
                }

                links.Add(
                    new BatchLink(
                        resolved.Job,
                        BatchExecutor.Relativize(plan.Path, resolved.OutputPath)
                    )
                );
                generated++;
                await Console.Error.WriteLineAsync(
                    $"Generated: {resolved.OutputPath}".AsMemory(),
                    ct
                );
            }

            if (links.Count == 0)
            {
                continue;
            }

            var rewritten = BatchMarkdown.RewriteLinks(plan.Original, links);
            if (!string.Equals(rewritten, plan.Original, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(
                        plan.Path,
                        rewritten,
                        DetectBatchEncoding(plan.Path),
                        ct
                    )
                    .ConfigureAwait(false);
            }
        }

        await WriteBatchFailuresAsync(failures, ct).ConfigureAwait(false);
        await Console.Error.WriteLineAsync(
            $"Batch markdown done: {generated} generated, {failures.Count} failed.".AsMemory(),
            ct
        );
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool TryFindBatchFiles(
        string inputPath,
        out string inputRoot,
        out string[] files,
        out string error
    )
    {
        if (File.Exists(inputPath))
        {
            if (!IsMarkdownPath(inputPath))
            {
                inputRoot = string.Empty;
                files = [];
                error = $"Input file must use the .md or .mdx extension: {inputPath}";
                return false;
            }

            inputRoot = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            files = [inputPath];
            error = string.Empty;
            return true;
        }

        if (!Directory.Exists(inputPath))
        {
            inputRoot = string.Empty;
            files = [];
            error = $"Input path not found: {inputPath}";
            return false;
        }

        inputRoot = inputPath;
        files = Directory
            .EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
            .Where(IsMarkdownPath)
            .ToArray();
        error = string.Empty;
        return true;
    }

    private static bool IsMarkdownPath(string path) =>
        Path.GetExtension(path) is var extension
        && (
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mdx", StringComparison.OrdinalIgnoreCase)
        );

    private static void ResolveBatchOutputs(
        IReadOnlyList<BatchFilePlan> plans,
        string inputRoot,
        string outputDir,
        List<string> failures
    )
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var owners = new Dictionary<string, string>(comparer);

        foreach (var plan in plans)
        {
            foreach (var job in plan.Parsed.Jobs)
            {
                var output = ResolveBatchOutput(plan, job, inputRoot, outputDir, owners.Keys);
                if (output is null)
                {
                    failures.Add(
                        $"{plan.RelativePath}:{job.MarkerLine}: output escapes the output directory."
                    );
                    continue;
                }

                var owner = $"{plan.RelativePath}:{job.MarkerLine}";
                if (owners.TryGetValue(output, out var previous))
                {
                    failures.Add(
                        $"{owner}: output '{BatchExecutor.NormalizePath(Path.GetRelativePath(outputDir, output))}' is already produced by {previous}."
                    );
                    continue;
                }

                owners.Add(output, owner);
                plan.Jobs.Add(new BatchResolvedJob(job, output));
            }
        }
    }

    private static string? ResolveBatchOutput(
        BatchFilePlan plan,
        BatchParsedJob job,
        string inputRoot,
        string outputDir,
        ICollection<string> plannedOutputs
    )
    {
        if (job.OutputRelative is not null)
        {
            return BatchExecutor.ResolveOutput(outputDir, job.OutputRelative);
        }

        if (job.ExistingLinkTarget is not null)
        {
            var existing = BatchExecutor.ResolveMarkdownLink(plan.Path, job.ExistingLinkTarget);
            if (
                existing is not null
                && BatchExecutor.IsPathInside(outputDir, existing)
                && !string.IsNullOrWhiteSpace(Path.GetExtension(existing))
            )
            {
                return existing;
            }
        }

        var relativeMarkdown = Path.GetRelativePath(inputRoot, plan.Path);
        var relativeDirectory = Path.GetDirectoryName(relativeMarkdown) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(plan.Path);
        var index = 1;
        while (index < int.MaxValue)
        {
            var relativeOutput = Path.Combine(relativeDirectory, $"{stem}-{index}.svg");
            var candidate = BatchExecutor.ResolveOutput(outputDir, relativeOutput);
            if (candidate is null)
            {
                return null;
            }

            if (!File.Exists(candidate) && !plannedOutputs.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }

        return null;
    }

    private static AppOptions BuildBatchJobOptions(AppOptions defaults, BatchParsedJob job)
    {
        var jobOptions = job.CaptureOptions.ShallowClone();
        jobOptions.Verbose = defaults.Verbose;
        jobOptions.VerboseLogPath = defaults.VerboseLogPath;
        jobOptions.Command = FirstBatchLine(job.Capture);
        if (string.IsNullOrWhiteSpace(jobOptions.Prompt))
        {
            jobOptions.Prompt = GetDefaultPrompt();
        }

        return jobOptions;
    }

    private static async Task ValidateFilteredSharedOutputsAsync(
        IReadOnlyList<string> allFiles,
        IEnumerable<string> selectedFiles,
        IReadOnlyList<BatchFilePlan> selectedPlans,
        string inputRoot,
        string outputDir,
        List<string> failures,
        CancellationToken ct
    )
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var selected = selectedFiles.ToHashSet(comparer);
        var excludedProducers = new Dictionary<string, string>(comparer);

        foreach (var path in allFiles.Where(path => !selected.Contains(path)))
        {
            var markdown = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var parsed = BatchMarkdown.Parse(markdown, path);
            foreach (var job in parsed.Jobs.Where(job => job.OutputRelative is not null))
            {
                var output = BatchExecutor.ResolveOutput(outputDir, job.OutputRelative!);
                if (output is null)
                {
                    continue;
                }

                var producer =
                    $"{BatchExecutor.NormalizePath(Path.GetRelativePath(inputRoot, path))}:{job.MarkerLine}";
                if (!excludedProducers.TryAdd(output, producer))
                {
                    excludedProducers[output] = "multiple excluded markers";
                }
            }
        }

        foreach (var plan in selectedPlans)
        {
            foreach (var target in BatchMarkdown.FindImageTargets(plan.Original))
            {
                var resolved = BatchExecutor.ResolveMarkdownLink(plan.Path, target);
                if (
                    resolved is null
                    || File.Exists(resolved)
                    || !excludedProducers.TryGetValue(resolved, out var producer)
                )
                {
                    continue;
                }

                failures.Add(
                    $"{plan.RelativePath}: shared output '{target}' is missing; producer {producer} was excluded by --filter."
                );
            }
        }
    }

    /// <summary>
    /// Returns null on success, otherwise an error message. Teardown is attempted
    /// after every setup/capture outcome, including timeout, failure, and cancellation.
    /// </summary>
    private static async Task<string?> ExecuteBatchJobAsync(
        BatchParsedJob job,
        AppOptions jobOptions,
        string outputPath,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken ct
    )
    {
        var setupLog =
            job.Setup.Length > 0
                ? Path.Combine(Path.GetTempPath(), $"c2s-batch-{Guid.NewGuid():N}.log")
                : null;
        string? result = null;
        string? teardownError = null;

        try
        {
            result = await ExecuteBatchJobCoreAsync(
                    job,
                    jobOptions,
                    outputPath,
                    setupLog,
                    loggerFactory,
                    logger,
                    ct
                )
                .ConfigureAwait(false);
        }
        finally
        {
            CleanupTempFile(setupLog, logger);
            if (job.Teardown.Length > 0)
            {
                using var teardownCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    var (exitCode, output) = await RunShellScriptAsync(
                            job.Teardown,
                            teardownCts.Token
                        )
                        .ConfigureAwait(false);
                    if (exitCode != 0)
                    {
                        teardownError = $"teardown exited with code {exitCode}: {output.Trim()}";
                    }
                }
                catch (OperationCanceledException)
                {
                    teardownError = "teardown timed out after 30 seconds.";
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Batch teardown failed.");
                    teardownError = $"teardown failed: {ex.Message}";
                }
            }
        }

        if (result is null)
        {
            return teardownError;
        }

        return teardownError is null ? result : result + " " + teardownError;
    }

    private static async Task<string?> ExecuteBatchJobCoreAsync(
        BatchParsedJob job,
        AppOptions jobOptions,
        string outputPath,
        string? setupLog,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken ct
    )
    {
        jobOptions.OutputPath = outputPath;
        var script = BatchExecutor.BuildScript(
            job,
            setupLog,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        );
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
                        replaySavePath: jobOptions.ReplaySavePath,
                        replayPath: jobOptions.ReplayPath,
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
            return "setup failed before capture started." + hint;
        }

        try
        {
            var renderOptions = SvgRenderOptionsFactory.Create(jobOptions);
            var outputExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(outputExtension) || outputExtension == "svg")
            {
                EnsureDirectory(outputPath);
                if (jobOptions.Mode is OutputMode.Video)
                {
                    await using var writer = new StreamWriter(
                        outputPath,
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
                            outputPath,
                            svg,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                            ct
                        )
                        .ConfigureAwait(false);
                }
            }
            else
            {
                var ffmpegPath = FindFfmpegExecutable();
                SvgConverter.SetFfmpegPath(ffmpegPath);
                SvgConverter.VerifyConversionPipeline(
                    jobOptions.SvgConverter,
                    RequiresFfmpeg(jobOptions, outputExtension),
                    logger
                );
                var converter = SvgConverter.ResolveConverter(
                    jobOptions.SvgConverter,
                    ffmpegAvailableOverride: SvgConverter.IsFfmpegAvailable,
                    logger
                );
                var useVideoPath = jobOptions.IsModeExplicit
                    ? jobOptions.Mode is OutputMode.Video
                    : IsVideoFormat(outputExtension);
                renderOptions.RenderCursor = useVideoPath;
                EnsureDirectory(outputPath);

                if (useVideoPath)
                {
                    await SvgConverter
                        .ConvertSvgFramesToVideoAsync(
                            RenderFrameSvgs(
                                session,
                                renderOptions,
                                jobOptions.VideoFps,
                                ct,
                                includeFallback: true
                            ),
                            jobOptions.VideoFps,
                            outputPath,
                            converter,
                            ffmpegPath,
                            jobOptions.SizeWidth,
                            jobOptions.SizeHeight,
                            logger,
                            ConsoleProgressReporter.Instance,
                            ct
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    var temporarySvg = Path.Combine(
                        Path.GetTempPath(),
                        $"c2s-batch-{Guid.NewGuid():N}.svg"
                    );
                    try
                    {
                        await File.WriteAllTextAsync(
                                temporarySvg,
                                SvgRenderer.Render(session, renderOptions),
                                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                ct
                            )
                            .ConfigureAwait(false);
                        await SvgConverter
                            .ConvertSvgToImageAsync(
                                temporarySvg,
                                outputPath,
                                converter,
                                ffmpegPath,
                                jobOptions.SizeWidth,
                                jobOptions.SizeHeight,
                                logger,
                                ConsoleProgressReporter.Instance,
                                ct
                            )
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        CleanupTempFile(temporarySvg, logger);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, $"Batch render failed for {outputPath}");
            return $"render failed: {ex.Message}";
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
            startInfo.Arguments = $"/d /s /c \"{script}\"";
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

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return (process.ExitCode, output + error);
        }
        catch (OperationCanceledException)
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
                // Best-effort cleanup before propagating cancellation.
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The process may already have disappeared while being terminated.
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch
            {
                // Stream teardown is best effort after forced termination.
            }

            throw;
        }
    }

    private static async Task WriteBatchFailuresAsync(
        IEnumerable<string> failures,
        CancellationToken ct
    )
    {
        foreach (var failure in failures.OrderBy(message => message, StringComparer.Ordinal))
        {
            await Console.Error.WriteLineAsync($"error: {failure}".AsMemory(), ct);
        }
    }

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
                Span<byte> preamble = stackalloc byte[3];
                _ = stream.Read(preamble);
                if (preamble.SequenceEqual(Encoding.UTF8.Preamble))
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
