namespace VoltTray.Core.Telemetry;

public interface IUserSettingsStore
{
    UpsModelPreset? LoadSelectedModel();
    void SaveSelectedModel(UpsModelPreset preset);
}
