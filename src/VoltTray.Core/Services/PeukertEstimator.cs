using System;
using VoltTray.Core.Ports;

namespace VoltTray.Core.Services;

public sealed class PeukertEstimator : IBatteryAutonomyEstimator
{
    public const double NominalVoltageDc = 24.0;
    public const double NominalCapacityAh = 9.0;
    public const double PeukertExponent = 1.25;
    public const double InverterEfficiency = 0.85;
    public const double RatedActiveWatts = 1200.0;
    public const double RatingDischargeHours = 20.0;
    public const double MinCapacityFactor = 0.30;
    public const double MaxCapacityFactor = 1.00;
    public const int MaxCeilingMinutes = 240;
    public const int LowBatteryOverrideMinutes = 1;

    public int EstimateRuntimeMinutes(int loadPercentage, double capacityFactor = 1.0, bool isHardwareLow = false)
    {
        if (isHardwareLow)
            return LowBatteryOverrideMinutes;

        double fCap = Math.Clamp(capacityFactor, MinCapacityFactor, MaxCapacityFactor);
        double effectiveLoadPct = Math.Max(loadPercentage, 1);
        double pac = (effectiveLoadPct / 100.0) * RatedActiveWatts;
        double currentAmps = (pac / InverterEfficiency) / NominalVoltageDc;

        double tHours = RatingDischargeHours * Math.Pow(NominalCapacityAh / (currentAmps * RatingDischargeHours), PeukertExponent) * fCap;
        int tMinutes = (int)Math.Round(tHours * 60.0, MidpointRounding.AwayFromZero);

        return Math.Clamp(tMinutes, 1, MaxCeilingMinutes);
    }

    public int CalculateSoCPercentage(double batteryVoltageDc)
    {
        if (batteryVoltageDc >= 27.0) return 100;
        if (batteryVoltageDc <= 20.4) return 0;

        if (batteryVoltageDc >= 26.0)
            return (int)Math.Round(90.0 + (batteryVoltageDc - 26.0) * 10.0, MidpointRounding.AwayFromZero);
        if (batteryVoltageDc >= 25.0)
            return (int)Math.Round(70.0 + (batteryVoltageDc - 25.0) * 20.0, MidpointRounding.AwayFromZero);
        if (batteryVoltageDc >= 24.0)
            return (int)Math.Round(50.0 + (batteryVoltageDc - 24.0) * 20.0, MidpointRounding.AwayFromZero);
        if (batteryVoltageDc >= 23.0)
            return (int)Math.Round(30.0 + (batteryVoltageDc - 23.0) * 20.0, MidpointRounding.AwayFromZero);
        if (batteryVoltageDc >= 22.0)
            return (int)Math.Round(15.0 + (batteryVoltageDc - 22.0) * 15.0, MidpointRounding.AwayFromZero);

        return (int)Math.Clamp(Math.Round((batteryVoltageDc - 20.4) / (22.0 - 20.4) * 15.0, MidpointRounding.AwayFromZero), 1, 15);
    }

    public string FormatRuntimeString(int minutes, string cultureName = "en")
    {
        return $"≈ {minutes} min";
    }

    public string FormatRuntimeCaption(int loadPercentage, string cultureName = "en")
    {
        if (cultureName.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            return $"Autonomía a {loadPercentage} % de carga";

        return $"Runtime at {loadPercentage}% load";
    }
}
