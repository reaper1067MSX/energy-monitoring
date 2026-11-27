using System.Text.Json;
using VoltTray.Core.Telemetry;

namespace VoltTray.Infrastructure.Settings;

internal sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private readonly string _settingsFilePath;

    public JsonUserSettingsStore(string? settingsFilePath = null)
    {
        _settingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SettingsConstants.AppFolderName,
                SettingsConstants.SettingsFileName)
            : settingsFilePath;
    }

    public UpsModelPreset? LoadSelectedModel()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            UserSettingsDocument? document = JsonSerializer.Deserialize(json, UserSettingsJsonContext.Default.UserSettingsDocument);
            if (document is null)
            {
                return null;
            }

            return UpsModelCatalog.TryParseCliKey(document.SelectedModel, out UpsModelPreset preset)
                ? preset
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void SaveSelectedModel(UpsModelPreset preset)
    {
        UpsModelProfile profile = UpsModelCatalog.GetProfile(preset);
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new UserSettingsDocument
        {
            SelectedModel = profile.CliKey
        };

        string json = JsonSerializer.Serialize(document, UserSettingsJsonContext.Default.UserSettingsDocument);
        File.WriteAllText(_settingsFilePath, json);
    }
}
