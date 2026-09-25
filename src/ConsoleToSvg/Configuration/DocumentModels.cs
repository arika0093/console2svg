using System;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration.Writable;
using VYaml.Annotations;

namespace ConsoleToSvg.Configuration;

[
    OptionsModel(Id = "ConsoleToSvg.ConfigDocument", Version = 1, SupportMigration = false),
    YamlObject
]
public partial class ConfigDocument
{
    [Description("Shared console2svg options for compatible workflows.")]
    public ConsoleOptions? Options { get; set; }
}

[
    OptionsModel(Id = "ConsoleToSvg.ScenarioDocument", Version = 1, SupportMigration = false),
    YamlObject
]
public partial class ScenarioDocument
{
    [Description("Shared options applied before Scenario-specific options.")]
    public ConsoleOptions? Options { get; set; }

    [JsonRequired]
    [Description("The Scenario definition to execute.")]
    public ScenarioDefinition? Scenario { get; set; }
}

[YamlObject]
public partial class ConsoleOptions
{
    [Description("PTY dimensions in terminal cells.")]
    public TerminalOptions? Terminal { get; set; }

    [Description("Policies for console2svg-managed environment variables.")]
    public EnvironmentOptions? Environment { get; set; }

    [Description("Options for interactive terminal workflows.")]
    public InteractiveOptions? Interactive { get; set; }

    [Description("Options for the live HTTP server.")]
    public LiveServerOptions? LiveServer { get; set; }

    [Description("Capture behavior and image/video settings.")]
    public CaptureOptions? Capture { get; set; }

    [Description("Terminal rendering and masking behavior.")]
    public RenderOptions? Render { get; set; }

    [Description("SVG conversion backend selection.")]
    public ConverterOptions? Converter { get; set; }

    [Description("Rendered SVG appearance.")]
    public AppearanceOptions? Appearance { get; set; }
}

[YamlObject]
public partial class TerminalOptions
{
    [Description("PTY width in terminal cells.")]
    public int? Width { get; set; }

    [Description("PTY height in terminal rows.")]
    public int? Height { get; set; }
}

[YamlObject]
public partial class EnvironmentOptions
{
    [Description("Whether console2svg overwrites color-related environment variables.")]
    public string? Color { get; set; }

    [Description("Whether console2svg removes CI-related environment variables.")]
    public string? Ci { get; set; }
}

[YamlObject]
public partial class InteractiveOptions
{
    [Description("Whether mouse input is enabled for interactive terminal workflows.")]
    public bool? Mouse { get; set; }
}

[YamlObject]
public partial class LiveServerOptions
{
    [Description("Listen endpoint; an empty host binds to loopback.")]
    public string? Host { get; set; }
}

[YamlObject]
public partial class CaptureOptions
{
    [Description("Capture mode.")]
    public string? Mode { get; set; }

    [Description("Crop applied to captured output.")]
    public CropOptions? Crop { get; set; }

    [Description("Image capture settings.")]
    public ImageCaptureOptions? Image { get; set; }

    [Description("Video capture settings.")]
    public VideoCaptureOptions? Video { get; set; }
}

[YamlObject]
public partial class CropOptions
{
    public string? Top { get; set; }
    public string? Right { get; set; }
    public string? Bottom { get; set; }
    public string? Left { get; set; }
}

[YamlObject]
public partial class ImageCaptureOptions
{
    [Description("Frame index used for image capture.")]
    public int? Frame { get; set; }

    [Description("Timestamp in seconds used for image capture.")]
    public double? Time { get; set; }
}

[YamlObject]
public partial class VideoCaptureOptions
{
    [Description("Video frame rate in frames per second.")]
    public double? Fps { get; set; }

    [Description("Whether the generated video loops.")]
    public bool? Loop { get; set; }

    [Description("Start and end timestamps in seconds.")]
    public CaptureTimeRange? Time { get; set; }

    [Description("Video frame timing mode.")]
    public string? Timing { get; set; }

    [JsonConverter(typeof(CoalesceValueJsonConverter))]
    [Description("Frame coalescing interval in milliseconds, or 'auto'.")]
    public string? Coalesce { get; set; }

    [Description("Video fade-out duration in seconds.")]
    public double? Fadeout { get; set; }
}

public sealed class CoalesceValueJsonConverter : JsonConverter<string>
{
    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number => ReadNumber(ref reader),
            _ => throw new JsonException("Expected 'auto' or a non-negative number."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
        )
        {
            writer.WriteNumberValue(number);
            return;
        }

        writer.WriteStringValue(value);
    }

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        var value = reader.GetDouble();
        if (!double.IsFinite(value))
        {
            throw new JsonException("Numeric coalesce values must be finite.");
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

[YamlObject]
public partial class CaptureTimeRange
{
    public double? Start { get; set; }
    public double? End { get; set; }
}

[YamlObject]
public partial class RenderOptions
{
    [Description("Secret masking options.")]
    public MaskingOptions? Masking { get; set; }

    [Description("Text adjustment behavior during terminal rendering.")]
    public string? Adjust { get; set; }
}

[YamlObject]
public partial class MaskingOptions
{
    public bool? Auto { get; set; }
    public string[]? Strings { get; set; }
}

[YamlObject]
public partial class ConverterOptions
{
    [Description("SVG-to-raster/video converter selection.")]
    public string? SvgConverter { get; set; }
}

[YamlObject]
public partial class AppearanceOptions
{
    public FontOptions? Font { get; set; }
    public ImageSizeOptions? Size { get; set; }
    public string[]? Theme { get; set; }
    public string? Window { get; set; }
    public double? Padding { get; set; }
    public double? Margin { get; set; }
    public double? PcPadding { get; set; }
    public string[]? Background { get; set; }
    public double? Opacity { get; set; }
    public string? Forecolor { get; set; }
    public string? Backcolor { get; set; }
    public HeaderOptions? Header { get; set; }
}

[YamlObject]
public partial class FontOptions
{
    public string? Family { get; set; }
    public double? Size { get; set; }
}

[YamlObject]
public partial class ImageSizeOptions
{
    public double? Width { get; set; }
    public double? Height { get; set; }
}

[YamlObject]
public partial class HeaderOptions
{
    public string? Text { get; set; }
    public bool? WithCommand { get; set; }
    public string? Prompt { get; set; }
}

[YamlObject]
public partial class ScenarioDefinition
{
    [Description("Working directory for Scenario commands and launch.")]
    public WorkingDirectoryOptions? WorkingDir { get; set; }

    [Description("Commands run in the control shell before launch.")]
    public ScenarioCommandStep[]? Prepare { get; set; }

    [JsonRequired]
    [Description("Executable and arguments launched in the application PTY.")]
    public ScenarioLaunch? Launch { get; set; }

    [Description("Actions performed in the application PTY.")]
    public ScenarioStep[]? Execute { get; set; }

    [Description("Commands run in the control shell after execution.")]
    public ScenarioCommandStep[]? Verify { get; set; }

    [Description("Commands run in the control shell after verification.")]
    public ScenarioCommandStep[]? Teardown { get; set; }
}

[YamlObject]
public partial class WorkingDirectoryOptions
{
    public string? Path { get; set; }
    public bool? Temporary { get; set; }
}

[YamlObject]
public partial class ScenarioCommandStep
{
    [Description("Command step type.")]
    public string? Type { get; set; }

    [Description("Shell command to run.")]
    public string? Command { get; set; }
}

[YamlObject]
public partial class ScenarioLaunch
{
    [JsonRequired]
    [Description("Executable launched in the application PTY.")]
    public string? Executable { get; set; }

    [Description("Arguments passed separately to the executable.")]
    public string[]? Args { get; set; }

    [Description("Terminal options applied to the launched PTY.")]
    public ConsoleOptions? Options { get; set; }
}

[YamlObject]
public partial class ScenarioStep
{
    [JsonRequired]
    [Description("Execution action: send, wait, resize, capture, or command.")]
    public string? Type { get; set; }

    [Description("Input events for send steps.")]
    public ScenarioInput[]? Inputs { get; set; }

    [Description("Arguments for wait and resize steps.")]
    public ScenarioStepArguments? Args { get; set; }

    [Description("Shell command for command steps.")]
    public string? Command { get; set; }

    [Description("Capture, render, appearance, and converter options for capture steps.")]
    public ConsoleOptions? Options { get; set; }
}

[YamlObject]
public partial class ScenarioInput
{
    public string? Text { get; set; }
    public string? Keys { get; set; }
    public string? RawHex { get; set; }
}

[YamlObject]
public partial class ScenarioStepArguments
{
    [Description("Text matched by wait steps.")]
    public string? Text { get; set; }

    [Description("Regular expression matched by wait steps.")]
    public string? Regex { get; set; }

    [Description("Whether the requested text or pattern must be present or absent.")]
    public string? Until { get; set; }

    [Description("Duration for which a match must remain stable.")]
    public string? StableFor { get; set; }

    [Description("Maximum duration to wait.")]
    public string? Timeout { get; set; }

    [Description("Output file path for capture steps.")]
    public string? Output { get; set; }

    [Description("PTY width in terminal cells for resize steps.")]
    public int? Width { get; set; }

    [Description("PTY height in terminal rows for resize steps.")]
    public int? Height { get; set; }
}
