namespace VoltTray.Shared;

public static class ElectricalConstants
{
    public const double NominalBlockVoltageDc = 12.0;
    public const int NominalBlockVoltageInt = 12;
    public const double NominalLeadAcidCellVolts = 2.0;
    public const double PercentageScaleDivisor = 100.0;
    public const int MinimumLoadPercentage = 0;
    public const int MaximumLoadPercentage = 100;
    public const double MinPlausibleFrequencyHz = 40.0;
    public const double MaxPlausibleFrequencyHz = 70.0;
    public const double NominalFrequency50Hz = 50.0;
    public const double NominalFrequency60Hz = 60.0;
    public const int VoltageDecimalPrecision = 1;
    public const int BatteryVoltageDecimalPrecision = 2;
    public const int FrequencyDecimalPrecision = 2;
    public const int BitsPerByte = 8;
    public const int MaxBigEndianByteWidth = 8;
    public const double DefaultFallbackVoltageAc = 120.0;
    public const double MaxSelfTestDurationSeconds = 15.0;
    public const double MinutesPerHour = 60.0;
}
