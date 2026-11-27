namespace VoltTray.Core.Telemetry;

public interface IUserSettingsStore
{
    UpsModelPreset? LoadSelectedModel();
    void SaveSelectedModel(UpsModelPreset preset);
    TrayIconMode? LoadIconMode();
    void SaveIconMode(TrayIconMode mode);
    ThemePreference LoadThemePreference();
    void SaveThemePreference(ThemePreference preference);
}
