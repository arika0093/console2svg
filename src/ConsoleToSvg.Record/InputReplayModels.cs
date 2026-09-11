using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Configuration.Writable;

namespace ConsoleToSvg.Recording;

/// <summary>
/// Source-generated JSON serializer context for the input replay data.
/// </summary>
[JsonSerializable(typeof(InputReplayData))]
[JsonSerializable(typeof(InputEvent))]
internal partial class InputReplaySerializerContext : JsonSerializerContext { }

/// <summary>
/// Source-generated metadata used only by the JSON v1 compatibility reader.
/// The fallback provider ignores its conventional <c>version</c> payload
/// property; only <c>$version</c> is Configuration.Writable schema metadata.
/// </summary>
[JsonSerializable(typeof(ReplayDocumentV1))]
internal partial class ReplayV1JsonContext : JsonSerializerContext { }

/// <summary>Cross-platform keyboard input event stored in replay files.</summary>
public sealed class InputEvent
{
    /// <summary>
    /// Absolute time in seconds from recording start.
    /// Required for the first event. Takes priority over <see cref="Tick"/> when both are specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("time")]
    public double? Time { get; set; }

    /// <summary>
    /// Delta time in seconds from the previous event.
    /// Used for subsequent events instead of <see cref="Time"/>.
    /// Ignored when <see cref="Time"/> is also present.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tick")]
    public double? Tick { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("modifiers")]
    public string[] Modifiers { get; set; } = [];

    [JsonPropertyName("type")]
    public string Type { get; set; } = "keydown";
}

/// <summary>Wrapper type for the JSON replay file.</summary>
public sealed class InputReplayData
{
    /// <summary>Current replay file format version written to new files.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Format version of this replay file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    /// <summary>UTC date and time when this replay file was created.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Total duration in seconds of the recording session.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("totalDuration")]
    public double? TotalDuration { get; set; }

    [JsonPropertyName("replay")]
    public List<InputEvent> Replay { get; set; } = [];
}

/// <summary>
/// The JSON v1 wire representation. This deliberately retains the legacy
/// string version property so old files can be migrated without rewriting
/// their input events before the migration runs.
/// </summary>
[OptionsModel(Id = "console2svg.replay", Version = 1)]
public sealed partial class ReplayDocumentV1
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("totalDuration")]
    public double? TotalDuration { get; set; }

    [JsonPropertyName("replay")]
    public List<InputEvent> Replay { get; set; } = [];
}
