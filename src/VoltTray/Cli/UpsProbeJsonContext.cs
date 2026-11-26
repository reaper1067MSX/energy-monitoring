using System.Text.Json.Serialization;

namespace VoltTray.Cli;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UpsProbeCaptureDocument))]
internal partial class UpsProbeJsonContext : JsonSerializerContext
{
}
