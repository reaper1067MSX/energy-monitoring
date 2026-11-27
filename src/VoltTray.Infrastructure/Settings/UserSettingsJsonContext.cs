using System.Text.Json.Serialization;

namespace VoltTray.Infrastructure.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserSettingsDocument))]
internal sealed partial class UserSettingsJsonContext : JsonSerializerContext
{
}
