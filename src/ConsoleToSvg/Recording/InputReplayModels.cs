using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Source-generated JSON serializer context for the input replay data.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(InputReplayData))]
[JsonSerializable(typeof(InputEvent))]
internal partial class InputReplaySerializerContext : JsonSerializerContext { }

/// <summary>Cross-platform keyboard input event stored in replay files.</summary>
public sealed class InputEvent
{
    public double Time { get; set; }
    public string Key { get; set; } = "";
    public string[] Modifiers { get; set; } = [];
    public string Type { get; set; } = "keydown";
}

/// <summary>Wrapper type for the JSON replay file.</summary>
public sealed class InputReplayData
{
    public List<InputEvent> Replay { get; set; } = [];
}
