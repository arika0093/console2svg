using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                    WriteThemeList(new ThemeCatalog(), options.OutputFormat);
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

    private static void WriteThemeList(ThemeCatalog catalog, Cli.OutputFormat format)
    {
        var entries = catalog
            .Entries.Select(entry => new ThemeListItem(
                entry.Manifest.Id ?? "",
                entry.Manifest.Name ?? "",
                entry.Manifest.Version ?? "",
                entry.IsBuiltIn ? "built-in" : "installed"
            ))
            .ToArray();
        if (format == Cli.OutputFormat.Json)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(entries, ThemeListJsonContext.Default.ThemeListItemArray)
            );
            return;
        }
        if (format == Cli.OutputFormat.Markdown)
        {
            Console.WriteLine("| ID | Name | Version | Source |");
            Console.WriteLine("| --- | --- | --- | --- |");
            foreach (var entry in entries)
                Console.WriteLine(
                    $"| {EscapeMarkdown(entry.Id)} | {EscapeMarkdown(entry.Name)} | {EscapeMarkdown(entry.Version)} | {entry.Source} |"
                );
            return;
        }

        var headers = new[] { "ID", "Name", "Version", "Source" };
        var widths = headers
            .Select(
                (header, index) =>
                    entries
                        .Select(entry =>
                            index switch
                            {
                                0 => entry.Id.Length,
                                1 => entry.Name.Length,
                                2 => entry.Version.Length,
                                _ => entry.Source.Length,
                            }
                        )
                        .Append(header.Length)
                        .Max()
            )
            .ToArray();

        WriteRow(headers, widths);
        WriteRow(widths.Select(width => new string('-', width)).ToArray(), widths);
        foreach (var entry in entries)
            WriteRow([entry.Id, entry.Name, entry.Version, entry.Source], widths);
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

internal sealed record ThemeListItem(string Id, string Name, string Version, string Source);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ThemeListItem[]))]
internal sealed partial class ThemeListJsonContext : JsonSerializerContext { }
