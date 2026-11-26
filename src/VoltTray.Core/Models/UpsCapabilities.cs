namespace VoltTray.Core.Models;

public sealed record UpsCapabilities(
    string? ModelName,
    double? NominalVoltageAc,
    double? NominalFrequencyHz,
    int? RatedWatts,
    BatteryBankSpec Bank,
    bool DialectVerified);
