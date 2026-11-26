using VoltTray.Core.Battery;
namespace VoltTray.Core.Telemetry;

public sealed record UpsCapabilities(
    string? ModelName,
    double? NominalVoltageAc,
    double? NominalFrequencyHz,
    int? RatedWatts,
    BatteryBankSpec Bank,
    bool DialectVerified);
