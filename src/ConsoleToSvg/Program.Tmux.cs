using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg;

internal static partial class Program
{
    private sealed record TmuxPane(
        string Id,
        string Label,
        string Title,
        string Command,
        int Width,
        int Height
    );

    private static async Task<string?> PrepareTmuxAsync(AppOptions options)
    {
        if (OperatingSystem.IsWindows())
            return "The tmux workflow is supported on Unix-like platforms only. Run console2svg inside WSL to use tmux.";

        TmuxPane? pane = null;
        if (!string.IsNullOrWhiteSpace(options.TmuxTarget))
        {
            pane = await GetTmuxPaneAsync(options.TmuxTarget).ConfigureAwait(false);
            if (pane is null)
                return $"tmux pane not found: {options.TmuxTarget}";
        }
        else
        {
            var panes = await GetTmuxPanesAsync().ConfigureAwait(false);
            var currentPane = Environment.GetEnvironmentVariable("TMUX_PANE");
            if (!string.IsNullOrWhiteSpace(currentPane))
                panes.RemoveAll(pane => pane.Id == currentPane);
            if (panes.Count == 0)
                return "No other tmux panes found. Start another pane, or specify a valid --target.";
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                return "--target is required when a tmux pane cannot be selected interactively.";
            pane = SelectTmuxPane(panes);
            if (pane is null)
                return "Tmux pane selection cancelled.";
        }

        options.TmuxTarget = pane.Id;
        if (options.WidthAdjust)
        {
            options.Width = pane.Width;
            options.WidthAdjust = false;
        }
        if (options.HeightAdjust)
        {
            options.Height = pane.Height;
            options.HeightAdjust = false;
        }

        var capture = BuildTmuxCaptureCommand(
            pane.Id,
            options.TmuxHistory,
            options.TmuxHistoryLines
        );
        if (options.RequestedTmuxAction == TmuxAction.LiveServer)
        {
            if (options.TmuxHistory)
                return "--history is not supported by tmux live-server.";
            options.Workflow = Workflow.LiveServer;
            options.LiveServerForwardToConsole = false;
            options.LiveServerResize = false;
            options.DelimitedCommand =
            [
                "sh",
                "-c",
                BuildTmuxPollingCommand(capture, options.VideoFps),
            ];
            return null;
        }

        options.Workflow = Workflow.Capture;
        options.Command =
            options.Mode == OutputMode.Video
                ? BuildTmuxPollingCommand(capture, options.VideoFps)
                : capture;
        return null;
    }

    private static string BuildTmuxCaptureCommand(string target, bool history, int? historyLines)
    {
        var historyArgument = string.Empty;
        if (history)
        {
            historyArgument = historyLines is int lines ? $" -S -{lines}" : " -S -";
        }
        return $"tmux capture-pane -p -e -t {ShellQuote(target)}{historyArgument}";
    }

    private static string BuildTmuxPollingCommand(string captureCommand, double fps)
    {
        var seconds = 1d / Math.Max(0.1d, fps);
        var interval = seconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"while :; do printf '\\033[2J\\033[H'; {captureCommand}; sleep {interval}; done";
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\\"'\\\"'", StringComparison.Ordinal) + "'";

    private static async Task<List<TmuxPane>> GetTmuxPanesAsync()
    {
        const string format =
            "#{pane_id}\t#{session_name}:#{window_index}.#{pane_index}\t#{pane_title}\t#{pane_current_command}\t#{pane_width}\t#{pane_height}";
        var tmuxPath = FindExecutableInPath("tmux");
        if (tmuxPath is null)
            return [];
        var startInfo = new ProcessStartInfo(tmuxPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("list-panes");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("-F");
        startInfo.ArgumentList.Add(format);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return [];
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
                return [];
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split('\t'))
                .Where(parts =>
                    parts.Length == 6
                    && int.TryParse(
                        parts[4],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out _
                    )
                    && int.TryParse(
                        parts[5],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out _
                    )
                )
                .Select(parts => new TmuxPane(
                    parts[0],
                    parts[1],
                    parts[2],
                    parts[3],
                    int.Parse(parts[4], CultureInfo.InvariantCulture),
                    int.Parse(parts[5], CultureInfo.InvariantCulture)
                ))
                .ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static async Task<TmuxPane?> GetTmuxPaneAsync(string target)
    {
        const string format =
            "#{pane_id}\t#{session_name}:#{window_index}.#{pane_index}\t#{pane_title}\t#{pane_current_command}\t#{pane_width}\t#{pane_height}";
        var tmuxPath = FindExecutableInPath("tmux");
        if (tmuxPath is null)
            return null;
        var startInfo = new ProcessStartInfo(tmuxPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("display-message");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("-F");
        startInfo.ArgumentList.Add(format);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var parts = output.TrimEnd('\r', '\n').Split('\t');
            if (
                process.ExitCode != 0
                || parts.Length != 6
                || !int.TryParse(
                    parts[4],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var width
                )
                || !int.TryParse(
                    parts[5],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var height
                )
            )
            {
                return null;
            }
            return new TmuxPane(parts[0], parts[1], parts[2], parts[3], width, height);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static TmuxPane? SelectTmuxPane(IReadOnlyList<TmuxPane> panes)
    {
        var selected = 0;
        var top = Console.CursorTop;
        var labelWidth = panes.Max(pane => pane.Label.Length);
        var commandWidth = panes.Max(pane => pane.Command.Length);
        var sizeWidth = panes.Max(pane => $"{pane.Width}x{pane.Height}".Length);
        try
        {
            while (true)
            {
                Console.SetCursorPosition(0, top);
                Console.WriteLine(
                    "Select a tmux pane (↑/↓, Enter; Esc to cancel):".PadRight(
                        Math.Max(1, Console.WindowWidth - 1)
                    )
                );
                for (var index = 0; index < panes.Count; index++)
                {
                    var pane = panes[index];
                    var prefix = index == selected ? "> " : "  ";
                    var dimensions = $"{pane.Width}x{pane.Height}";
                    var text =
                        $"{prefix}{pane.Label.PadRight(labelWidth)}  {pane.Command.PadRight(commandWidth)}  {dimensions.PadRight(sizeWidth)}  {TruncateTmuxTitle(pane.Title)}";
                    Console.WriteLine(
                        text.Length >= Console.WindowWidth
                            ? text[..Math.Max(0, Console.WindowWidth - 1)]
                            : text.PadRight(Math.Max(1, Console.WindowWidth - 1))
                    );
                }
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Enter)
                    return panes[selected];
                if (key == ConsoleKey.Escape)
                    return null;
                if (key == ConsoleKey.UpArrow)
                    selected = (selected + panes.Count - 1) % panes.Count;
                if (key == ConsoleKey.DownArrow)
                    selected = (selected + 1) % panes.Count;
            }
        }
        finally
        {
            Console.SetCursorPosition(0, top + panes.Count + 1);
        }
    }

    private static string TruncateTmuxTitle(string title)
    {
        const int maxLength = 32;
        return title.Length <= maxLength ? title : title[..(maxLength - 3)] + "...";
    }
}
