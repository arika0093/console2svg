using System;
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
                    Console.WriteLine("ID\tName\tVersion\tSource");
                    foreach (var entry in new ThemeCatalog().Entries)
                        Console.WriteLine(
                            $"{entry.Manifest.Id}\t{entry.Manifest.Name}\t{entry.Manifest.Version ?? ""}\t{(entry.IsBuiltIn ? "built-in" : "installed")}"
                        );
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
}
