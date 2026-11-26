using System;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Shared;

namespace VoltTray.Core.Services;

public sealed class PeukertEstimator : IBatteryAutonomyEstimator
{
    public const double PeukertExponent = 1.25;
    public const double InverterEfficiency = 0.85;
    public const double RatingDischargeHours = 20.0;
    public const double MinCapacityFactor = 0.30;
    public const double MaxCapacityFactor = 1.00;
    public const int MaxCeilingMinutes = 240;
    public const int LowBatteryOverrideMinutes = 1;
    public const int DefaultSeriesBatteryCount = 2;

    private const int MinimumLoadPercentage = 1;
    private const double MinutesPerHour = 60.0;
    private const int EmptySoCPercentage = 0;

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
            return LowBatteryOverrideMinutes;
        }

        if (capabilities == null ||
            capabilities.RatedWatts is null or <= 0 ||
            capabilities.Bank.RatedCapacityAh is null or <= 0 ||
            capabilities.Bank.NominalVoltageDc <= 0)
        {
            return null;
        }

        double clampedCapacityFactor = double.IsNaN(capacityFactor)
            ? MinCapacityFactor
            : Math.Clamp(capacityFactor, MinCapacityFactor, MaxCapacityFactor);

        double effectiveLoadPercentage = Math.Max(loadPercentage, MinimumLoadPercentage);
        double activeWatts = (effectiveLoadPercentage / ElectricalConstants.PercentageScaleDivisor) * capabilities.RatedWatts.Value;
        double currentAmperes = (activeWatts / InverterEfficiency) / capabilities.Bank.NominalVoltageDc;

        double runtimeHours =
            RatingDischargeHours *
            Math.Pow(capabilities.Bank.RatedCapacityAh.Value / (currentAmperes * RatingDischargeHours), PeukertExponent) *
            clampedCapacityFactor;

        int runtimeMinutes = (int)Math.Round(runtimeHours * MinutesPerHour, MidpointRounding.AwayFromZero);
        return Math.Clamp(runtimeMinutes, LowBatteryOverrideMinutes, MaxCeilingMinutes);
    }

    public int CalculateSoCPercentage(double batteryVoltageDc, int seriesBatteryCount = DefaultSeriesBatteryCount)
    {
        if (double.IsNaN(batteryVoltageDc))
        {
            return EmptySoCPercentage;
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

            return lower.Percentage == EmptySoCPercentage
                ? Math.Clamp(roundedPercentage, LowBatteryOverrideMinutes, upper.Percentage)
                : roundedPercentage;
        }

        return EmptySoCPercentage;
    }
}
