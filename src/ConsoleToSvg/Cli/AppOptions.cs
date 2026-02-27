namespace ConsoleToSvg.Cli;

public enum OutputMode
{
    Image,
    Video,
}

public sealed class AppOptions
{
    public bool Verbose { get; set; }

    public bool ShowVersion { get; set; }

    public string? Command { get; set; }

    public string? InputCastPath { get; set; }

    public string OutputPath { get; set; } = "output.svg";

    public OutputMode Mode { get; set; } = OutputMode.Image;

    public int? Width { get; set; } = null;

    public int? Height { get; set; } = null;

    public int? Frame { get; set; }

    public string CropTop { get; set; } = "0";

    public string CropRight { get; set; } = "0";

    public string CropBottom { get; set; } = "0";

    public string CropLeft { get; set; } = "0";

    public string Theme { get; set; } = "dark";

    public string? SaveCastPath { get; set; }

    public string? Font { get; set; }

    public string Window { get; set; } = "none";

    public double? Padding { get; set; }

    public bool Loop { get; set; } = true;

    public double VideoFps { get; set; } = 12d;

    public double VideoSleep { get; set; } = 2d;

    public double VideoFadeOut { get; set; } = 0d;

    public double Opacity { get; set; } = 1d;

    public bool WithCommand { get; set; }

    public System.Collections.Generic.List<string> Background { get; set; } = [];
}