using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleToSvg.Tests.Cli;

public sealed class CliBindingTests
{
    [Test]
    public async Task RootHelpListsOnlyCommands()
    {
        var result = await RunCliAsync("--help");

        result.ExitCode.ShouldBe(0);
        result.StdOut.ShouldContain("Commands:");
        result.StdOut.ShouldContain("capture    Capture command output");
        result.StdOut.ShouldContain("record     Record a raw terminal session");
        result.StdOut.ShouldNotContain("--width");
        result.StdOut.ShouldNotContain("compat");
        result.StdOut.ShouldNotContain("\x1b[");
    }

    [Test]
    public async Task CaptureHelpIsGeneratedFromItsTypedSignature()
    {
        var result = await RunCliAsync("capture", "--help");

        result.ExitCode.ShouldBe(0);
        result.StdOut.ShouldContain("-o, --out");
        result.StdOut.ShouldContain("-w, --width");
        result.StdOut.ShouldContain("-v, --video, --animation");
        result.StdOut.ShouldContain("--height");
        result.StdOut.ShouldNotContain("-h, --height");
        result.StdOut.ShouldNotContain("--in ");
        result.StdOut.ShouldNotContain("--interactive");
        result.StdOut.ShouldNotContain("\x1b[");
    }

    [Test]
    public async Task RecordRejectsAnIrrelevantRenderOption()
    {
        var result = await RunCliAsync("record", "session.cast", "--format", "svg");

        result.ExitCode.ShouldBe(1);
        result.StdErr.ShouldContain("Argument '--format' is not recognized.");
    }

    [Test]
    public async Task CaptureBindsTypedOptionsAndEscapedCommand()
    {
        var result = await RunCliAsync(
            "capture",
            "--format",
            "svg",
            "--width",
            "40",
            "--height",
            "5",
            "--size",
            "320x*",
            "--stdout",
            "--",
            "printf",
            "bound"
        );

        result.ExitCode.ShouldBe(0);
        result.StdOut.ShouldContain("<svg");
        result.StdOut.ShouldContain("width=\"320\"");
        result.StdOut.ShouldContain("bound");
    }

    [Test]
    public async Task OmittedCaptureVerbReadsRedirectedInput()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "output.svg");
        File.Delete(outputPath);
        try
        {
            var result = await RunCliWithInputAsync("piped");

            result.ExitCode.ShouldBe(0);
            File.Exists(outputPath).ShouldBeTrue();
            (await File.ReadAllTextAsync(outputPath)).ShouldContain("piped");
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Test]
    public async Task RecordAndRenderUsePositionalFiles()
    {
        var id = Guid.NewGuid().ToString("N");
        var castPath = Path.Combine(AppContext.BaseDirectory, $"{id}.cast");
        var svgPath = Path.Combine(AppContext.BaseDirectory, $"{id}.svg");
        try
        {
            var record = await RunCliAsync(
                "record",
                castPath,
                "--",
                "printf",
                "workflow"
            );
            var render = await RunCliAsync("render", castPath, "-o", svgPath);

            record.ExitCode.ShouldBe(0);
            render.ExitCode.ShouldBe(0);
            (await File.ReadAllTextAsync(svgPath)).ShouldContain("workflow");
        }
        finally
        {
            File.Delete(castPath);
            File.Delete(svgPath);
        }
    }

    [Test]
    public async Task ReplayRequiresACommandAfterTheDelimiter()
    {
        var result = await RunCliAsync("replay", "input.replay.json");

        result.ExitCode.ShouldBe(1);
        result.StdErr.ShouldContain("requires a command after --");
    }

    private static async Task<CliResult> RunCliAsync(params string[] arguments)
    {
        return await RunCliAsync(null, arguments);
    }

    private static async Task<CliResult> RunCliWithInputAsync(string input)
    {
        return await RunCliAsync(input, []);
    }

    private static async Task<CliResult> RunCliAsync(string? input, string[] arguments)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "console2svg.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return new(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false)
        );
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
