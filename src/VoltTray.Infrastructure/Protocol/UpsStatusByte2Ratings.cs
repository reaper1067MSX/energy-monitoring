namespace VoltTray.Infrastructure.Protocol;

/// <summary>
/// Decoded hardware nominal ratings from Voltronic Protocol 'T' Status Byte 2 (token 10).
/// </summary>
public readonly record struct UpsStatusByte2Ratings(
    double NominalFrequencyHz,
    int BatteryBankSeriesCount,
    double NominalBatteryVoltageDc,
    double? NominalOutputVoltageAc);
