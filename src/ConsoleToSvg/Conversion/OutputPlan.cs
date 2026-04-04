using System;
using System.IO;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg.Conversion;

internal sealed class OutputPlan
{
    private OutputPlan(OutputFormat format, string extension)
    {
        Format = format;
        Extension = extension;
    }

    public OutputFormat Format { get; }

    public string Extension { get; }

    public bool IsSvg => Format == OutputFormat.Svg;

    public bool RequiresResvg => Format is not OutputFormat.Svg;

    public bool RequiresFfmpeg => Format is not OutputFormat.Svg and not OutputFormat.Png;

    public bool RequiresAnimatedFrames => Format is OutputFormat.Gif or OutputFormat.Mp4 or OutputFormat.Webm;

    public bool RequiresStaticSvg => Format is OutputFormat.Png or OutputFormat.Jpeg or OutputFormat.Webp;

    public static OutputPlan Create(AppOptions options)
    {
        var extension = Path.GetExtension(options.OutputPath)?.ToLowerInvariant() ?? string.Empty;
        var format = extension switch
        {
            ".png" => OutputFormat.Png,
            ".jpg" => OutputFormat.Jpeg,
            ".jpeg" => OutputFormat.Jpeg,
            ".gif" => OutputFormat.Gif,
            ".mp4" => OutputFormat.Mp4,
            ".webm" => OutputFormat.Webm,
            ".webp" => OutputFormat.Webp,
            ".svg" or "" => OutputFormat.Svg,
            _ => OutputFormat.Svg,
        };

        var plan = new OutputPlan(format, extension);
        Validate(options, plan);
        return plan;
    }

    private static void Validate(AppOptions options, OutputPlan plan)
    {
        if (options.StdOut && !plan.IsSvg)
        {
            throw new InvalidOperationException("--stdout only supports SVG output.");
        }

        if (plan.RequiresAnimatedFrames && options.Mode == OutputMode.Image)
        {
            throw new InvalidOperationException(
                $"Animated output format '{DisplayExtension(plan.Extension)}' requires --mode video, --mode repeat, or -v."
            );
        }

        if (plan.RequiresStaticSvg && options.Mode != OutputMode.Image)
        {
            throw new InvalidOperationException(
                $"Static raster output format '{DisplayExtension(plan.Extension)}' requires image mode."
            );
        }
    }

    private static string DisplayExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? ".svg" : extension;
}
