using VoltTray.Core.Telemetry;
using System;
using VoltTray.Shared;

namespace VoltTray.Core.Battery;

public sealed class PeukertEstimator : IBatteryAutonomyEstimator
{
    private static readonly BatterySoCCurvePoint[] AgmDischargeCurve =
    [
        new(13.5, 100),
        new(13.0, 90),
        new(12.5, 70),
        new(12.0, 50),
        new(11.5, 30),
        new(11.0, 15),
        new(10.2, 0)
    ];

    public int? EstimateRuntimeMinutes(
        int loadPercentage,
        UpsCapabilities capabilities,
        double capacityFactor = 1.0,
        bool isHardwareLow = false)
    {
        if (isHardwareLow)
        {
            return BatteryConstants.LowBatteryOverrideMinutes;
        }

        if (capabilities == null ||
            capabilities.RatedWatts is null or <= 0 ||
            capabilities.Bank.RatedCapacityAh is null or <= 0 ||
            capabilities.Bank.NominalVoltageDc <= 0)
        {
            return null;
        }

        double clampedCapacityFactor = double.IsNaN(capacityFactor)
            ? BatteryConstants.MinCapacityFactor
            : Math.Clamp(capacityFactor, BatteryConstants.MinCapacityFactor, BatteryConstants.MaxCapacityFactor);

        double effectiveLoadPercentage = Math.Max(loadPercentage, BatteryConstants.MinimumEffectiveLoadPercentage);
        double activeWatts = (effectiveLoadPercentage / ElectricalConstants.PercentageScaleDivisor) * capabilities.RatedWatts.Value;
        double currentAmperes = (activeWatts / BatteryConstants.InverterEfficiency) / capabilities.Bank.NominalVoltageDc;

        double runtimeHours =
            BatteryConstants.RatingDischargeHours *
            Math.Pow(
                capabilities.Bank.RatedCapacityAh.Value / (currentAmperes * BatteryConstants.RatingDischargeHours),
                BatteryConstants.PeukertExponent) *
            clampedCapacityFactor;

        int runtimeMinutes = (int)Math.Round(
            runtimeHours * ElectricalConstants.MinutesPerHour,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(runtimeMinutes, BatteryConstants.LowBatteryOverrideMinutes, BatteryConstants.MaxCeilingMinutes);
    }

    public int CalculateSoCPercentage(
        double batteryVoltageDc,
        int seriesBatteryCount = BatteryConstants.DefaultSeriesBatteryCount)
    {
        if (double.IsNaN(batteryVoltageDc))
        {
            return BatteryConstants.EmptySoCPercentage;
        }

        int normalizedSeriesCount = Math.Max(1, seriesBatteryCount);
        double blockVoltageDc = batteryVoltageDc / normalizedSeriesCount;

        if (blockVoltageDc >= AgmDischargeCurve[0].BlockVoltageDc)
        {
            return AgmDischargeCurve[0].Percentage;
        }

        if (blockVoltageDc <= AgmDischargeCurve[^1].BlockVoltageDc)
        {
            return AgmDischargeCurve[^1].Percentage;
        }

        for (int index = 0; index < AgmDischargeCurve.Length - 1; index++)
        {
            BatterySoCCurvePoint upper = AgmDischargeCurve[index];
            BatterySoCCurvePoint lower = AgmDischargeCurve[index + 1];
            if (blockVoltageDc < lower.BlockVoltageDc)
            {
                continue;
            }

            double voltageFraction =
                (blockVoltageDc - lower.BlockVoltageDc) / (upper.BlockVoltageDc - lower.BlockVoltageDc);
            double interpolatedPercentage =
                lower.Percentage + (voltageFraction * (upper.Percentage - lower.Percentage));
            int roundedPercentage = (int)Math.Round(interpolatedPercentage, MidpointRounding.AwayFromZero);

            return lower.Percentage == BatteryConstants.EmptySoCPercentage
                ? Math.Clamp(roundedPercentage, BatteryConstants.LowBatteryOverrideMinutes, upper.Percentage)
                : roundedPercentage;
        }

        return BatteryConstants.EmptySoCPercentage;
    }
}
