using System;

namespace ConsoleToSvg.Terminal;

public sealed class Theme
{
    private Theme(string name, string background, string foreground, string[] ansiPalette)
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

    public static Theme Resolve(string? name)
    {
        if (string.Equals(name, "light", StringComparison.OrdinalIgnoreCase))
        {
            return Light;
        }

        return Dark;
    }

    private static Theme Dark =>
        new(
            "dark",
            background: "#1e1e1e",
            foreground: "#d4d4d4",
            ansiPalette:
            [
                "#1e1e1e",
                "#cd3131",
                "#0dbc79",
                "#e5e510",
                "#2472c8",
                "#bc3fbc",
                "#11a8cd",
                "#e5e5e5",
                "#666666",
                "#f14c4c",
                "#23d18b",
                "#f5f543",
                "#3b8eea",
                "#d670d6",
                "#29b8db",
                "#ffffff",
            ]
        );

    private static Theme Light =>
        new(
            "light",
            background: "#ffffff",
            foreground: "#1e1e1e",
            ansiPalette:
            [
                "#000000",
                "#cd3131",
                "#00bc00",
                "#949800",
                "#0451a5",
                "#bc05bc",
                "#0598bc",
                "#555555",
                "#666666",
                "#cd3131",
                "#14ce14",
                "#b5ba00",
                "#0451a5",
                "#bc05bc",
                "#0598bc",
                "#a5a5a5",
            ]
        );
}
