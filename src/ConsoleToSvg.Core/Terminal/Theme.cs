using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleToSvg.Terminal;

public sealed class Theme
{
    public Theme(string name, string background, string foreground, string[] ansiPalette)
    {
        Name = name;
        Background = background;
        Foreground = foreground;
        AnsiPalette = ansiPalette;
    }

    public string Name { get; }
    public string Background { get; }
    public string Foreground { get; }
    public string[] AnsiPalette { get; }

    public Theme WithBackground(string background) =>
        new(Name, background, Foreground, AnsiPalette);

    public Theme WithForeground(string foreground) =>
        new(Name, Background, foreground, AnsiPalette);

    public static Theme Resolve(string? name)
    {
        var id = string.IsNullOrWhiteSpace(name) ? "dark" : name;
        var entry = new ThemeCatalog().Resolve(id);
        var terminal =
            entry.Manifest.Terminal
            ?? throw new InvalidOperationException(
                $"Theme '{id}' does not define terminal colors."
            );
        return new Theme(
            entry.Manifest.Id!,
            terminal.Background!,
            terminal.Foreground!,
            terminal.AnsiPalette!.ToArray()
        );
    }

    public static Theme ResolveMany(IEnumerable<string> names)
    {
        var ids = names.ToArray();
        if (ids.Length == 0)
            return Resolve("dark");
        var catalog = new ThemeCatalog();
        var current = Resolve("dark");
        foreach (var id in ids)
        {
            var terminal = catalog.Resolve(id).Manifest.Terminal;
            if (terminal is null)
                continue;
            current = new Theme(
                id,
                terminal.Background ?? current.Background,
                terminal.Foreground ?? current.Foreground,
                terminal.AnsiPalette ?? current.AnsiPalette
            );
        }
        return current;
    }
}
