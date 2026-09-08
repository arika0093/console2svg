using System;
using System.Linq;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static int RunThemeCommand(Cli.AppOptions options)
    {
        try
        {
            var action = options.RequestedThemeAction ?? Cli.ThemeAction.List;
            switch (action)
            {
                case Cli.ThemeAction.List:
                    WriteThemeList(new ThemeCatalog());
                    return 0;
                case Cli.ThemeAction.Install:
                    ThemeManager.Install(options.ThemeArgument!);
                    Console.WriteLine($"Installed theme: {options.ThemeArgument}");
                    return 0;
                case Cli.ThemeAction.Remove:
                    ThemeManager.Remove(options.ThemeArgument!);
                    Console.WriteLine($"Removed theme: {options.ThemeArgument}");
                    return 0;
                case Cli.ThemeAction.Update:
                    ThemeManager.Update(options.ThemeArgument);
                    Console.WriteLine(
                        options.ThemeArgument is null
                            ? "Updated installed themes."
                            : $"Updated theme: {options.ThemeArgument}"
                    );
                    return 0;
                default:
                    return 1;
            }
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or System.IO.IOException or ArgumentException)
        {
            Console.Error.WriteLine($"theme: {ex.Message}");
            return 1;
        }
    }

    private static void WriteThemeList(ThemeCatalog catalog)
    {
        var rows = catalog
            .Entries.Select(entry =>
                new[]
                {
                    entry.Manifest.Id ?? "",
                    entry.Manifest.Name ?? "",
                    entry.Manifest.Version ?? "",
                    entry.IsBuiltIn ? "built-in" : "installed",
                }
            )
            .ToArray();
        var headers = new[] { "ID", "Name", "Version", "Source" };
        var widths = headers
            .Select(
                (header, index) => rows.Select(row => row[index].Length).Append(header.Length).Max()
            )
            .ToArray();

        WriteRow(headers, widths);
        WriteRow(widths.Select(width => new string('-', width)).ToArray(), widths);
        foreach (var row in rows)
            WriteRow(row, widths);
    }

    private static void WriteRow(string[] columns, int[] widths) =>
        Console.WriteLine(
            string.Join(
                "  ",
                columns.Select(
                    (column, index) =>
                        index == 2 ? column.PadLeft(widths[index]) : column.PadRight(widths[index])
                )
            )
        );
}
