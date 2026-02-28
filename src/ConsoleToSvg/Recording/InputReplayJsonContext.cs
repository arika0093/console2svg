using System.Text.Json.Serialization;

namespace ConsoleToSvg.Recording;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InputReplayData))]
[JsonSerializable(typeof(InputEvent))]
internal partial class InputReplayJsonContext : JsonSerializerContext { }
