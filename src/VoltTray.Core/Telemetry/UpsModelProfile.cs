using VoltTray.Core.Battery;

namespace VoltTray.Core.Telemetry;

public sealed record UpsModelProfile(
    UpsModelPreset Preset,
    string CliKey,
    string DisplayName,
    int RatedVa,
    int RatedWatts,
    BatteryBankSpec BankSpec)
{
    public UpsCapabilities ToCapabilities() => new(
        ModelName: DisplayName,
        NominalVoltageAc: null,
        NominalFrequencyHz: null,
        RatedWatts: RatedWatts,
        Bank: BankSpec,
        DialectVerified: false);
}
