using System;
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
    /// <summary>
    /// Absolute time in seconds from recording start.
    /// Required for the first event. Takes priority over <see cref="Tick"/> when both are specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Time { get; set; }

    /// <summary>
    /// Delta time in seconds from the previous event.
    /// Used for subsequent events instead of <see cref="Time"/>.
    /// Ignored when <see cref="Time"/> is also present.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Tick { get; set; }

    public string Key { get; set; } = "";
    public string[] Modifiers { get; set; } = [];
    public string Type { get; set; } = "keydown";
}

/// <summary>Wrapper type for the JSON replay file.</summary>
public sealed class InputReplayData
{
    /// <summary>Current replay file format version written to new files.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Format version of this replay file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    /// <summary>UTC date and time when this replay file was created.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Total duration in seconds of the recording session.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TotalDuration { get; set; }

    public List<InputEvent> Replay { get; set; } = [];
}
