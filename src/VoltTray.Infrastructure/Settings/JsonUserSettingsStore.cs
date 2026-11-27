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
        UserSettingsDocument? document = LoadDocument();
        if (document is null)
        {
            return null;
        }

        return UpsModelCatalog.TryParseCliKey(document.SelectedModel, out UpsModelPreset preset)
            ? preset
            : null;
    }

    public void SaveSelectedModel(UpsModelPreset preset)
    {
        UpsModelProfile profile = UpsModelCatalog.GetProfile(preset);
        UserSettingsDocument document = LoadDocument() ?? new UserSettingsDocument();
        document.SelectedModel = profile.CliKey;
        SaveDocument(document);
    }

    public TrayIconMode? LoadIconMode()
    {
        UserSettingsDocument? document = LoadDocument();
        if (document is null)
        {
            return null;
        }

        if (string.Equals(document.IconMode, SettingsConstants.LargeIconModeKey, StringComparison.OrdinalIgnoreCase))
        {
            return TrayIconMode.Large;
        }

        if (string.Equals(document.IconMode, SettingsConstants.ShortIconModeKey, StringComparison.OrdinalIgnoreCase))
        {
            return TrayIconMode.Short;
        }

        return null;
    }

    public void SaveIconMode(TrayIconMode mode)
    {
        UserSettingsDocument document = LoadDocument() ?? new UserSettingsDocument();
        document.IconMode = mode == TrayIconMode.Short
            ? SettingsConstants.ShortIconModeKey
            : SettingsConstants.LargeIconModeKey;
        SaveDocument(document);
    }

    public ThemePreference LoadThemePreference()
    {
        UserSettingsDocument? document = LoadDocument();
        if (document is null)
        {
            return ThemePreference.Auto;
        }

        if (string.Equals(document.ThemePreference, SettingsConstants.DarkThemeKey, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePreference.Dark;
        }

        if (string.Equals(document.ThemePreference, SettingsConstants.LightThemeKey, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePreference.Light;
        }

        return ThemePreference.Auto;
    }

    public void SaveThemePreference(ThemePreference preference)
    {
        UserSettingsDocument document = LoadDocument() ?? new UserSettingsDocument();
        document.ThemePreference = preference switch
        {
            ThemePreference.Dark => SettingsConstants.DarkThemeKey,
            ThemePreference.Light => SettingsConstants.LightThemeKey,
            _ => SettingsConstants.AutoThemeKey
        };
        SaveDocument(document);
    }

    private UserSettingsDocument? LoadDocument()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize(json, UserSettingsJsonContext.Default.UserSettingsDocument);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void SaveDocument(UserSettingsDocument document)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(document, UserSettingsJsonContext.Default.UserSettingsDocument);
        File.WriteAllText(_settingsFilePath, json);
    }
}
