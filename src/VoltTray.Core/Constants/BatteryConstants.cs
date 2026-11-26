namespace VoltTray.Core.Constants;

public static class BatteryConstants
{
    public const int RequiredConsecutiveReadings = 3;
    public const double PeukertExponent = 1.25;
    public const double InverterEfficiency = 0.85;
    public const double RatingDischargeHours = 20.0;
    public const double MinCapacityFactor = 0.30;
    public const double MaxCapacityFactor = 1.00;
    public const int MaxCeilingMinutes = 240;
    public const int LowBatteryOverrideMinutes = 1;
    public const int DefaultSeriesBatteryCount = 2;
    public const int MinimumEffectiveLoadPercentage = 1;
    public const int FullSoCPercentage = 100;
    public const int FifteenPercentSoC = 15;
    public const int EmptySoCPercentage = 0;
}
