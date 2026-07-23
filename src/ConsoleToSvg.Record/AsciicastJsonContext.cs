using System.Text.Json.Serialization;

namespace ConsoleToSvg.Recording;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AsciicastHeader))]
internal partial class AsciicastJsonContext : JsonSerializerContext { }
