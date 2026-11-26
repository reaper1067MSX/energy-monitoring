namespace VoltTray.Core.Models;

public sealed record BatteryBankSpec(
    int SeriesBatteryCount = 2,
    double NominalVoltageDc = 24.0,
    double? RatedCapacityAh = 9.0,
    string? Label = null);
