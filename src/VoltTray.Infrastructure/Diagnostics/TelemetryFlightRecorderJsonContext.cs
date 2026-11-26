using System.Text.Json.Serialization;

namespace VoltTray.Infrastructure.Diagnostics;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(TelemetryFlightRecorderEntry))]
internal partial class TelemetryFlightRecorderJsonContext : JsonSerializerContext
{
}
