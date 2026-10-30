namespace VoltTray.Core.Models;

public sealed record BatteryBankSpec(
    double NominalVoltageDc = 24.0,
    double RatedCapacityAh = 9.0,
    double NominalEnergyWh = 216.0,
    double InternalResistanceOhms = 0.036,
    string ModelName = "2x Forza FUB-1290");
