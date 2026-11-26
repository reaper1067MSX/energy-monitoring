using System;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;

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
    private const double PercentageScaleDivisor = 100.0;
    private const double MinutesPerHour = 60.0;

    private const double BlockFullVoltageDc = 13.5;
    private const double BlockNinetyPercentVoltageDc = 13.0;
    private const double BlockSeventyPercentVoltageDc = 12.5;
    private const double BlockFiftyPercentVoltageDc = 12.0;
    private const double BlockThirtyPercentVoltageDc = 11.5;
    private const double BlockFifteenPercentVoltageDc = 11.0;
    private const double BlockEmptyVoltageDc = 10.2;

    private const int FullSoCPercentage = 100;
    private const int NinetyPercentSoC = 90;
    private const int SeventyPercentSoC = 70;
    private const int FiftyPercentSoC = 50;
    private const int ThirtyPercentSoC = 30;
    private const int FifteenPercentSoC = 15;
    private const int EmptySoCPercentage = 0;

    private const double UpperBandSlopePerVolt = 20.0;
    private const double MiddleBandSlopePerVolt = 40.0;
    private const double LowerMidBandSlopePerVolt = 30.0;
    private const double CutoffBandSpanVolts = 0.8;

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
        double activeWatts = (effectiveLoadPercentage / PercentageScaleDivisor) * capabilities.RatedWatts.Value;
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

        if (blockVoltageDc >= BlockFullVoltageDc)
        {
            return FullSoCPercentage;
        }

        if (blockVoltageDc <= BlockEmptyVoltageDc)
        {
            return EmptySoCPercentage;
        }

        if (blockVoltageDc >= BlockNinetyPercentVoltageDc)
        {
            return (int)Math.Round(
                NinetyPercentSoC + ((blockVoltageDc - BlockNinetyPercentVoltageDc) * UpperBandSlopePerVolt),
                MidpointRounding.AwayFromZero);
        }

        if (blockVoltageDc >= BlockSeventyPercentVoltageDc)
        {
            return (int)Math.Round(
                SeventyPercentSoC + ((blockVoltageDc - BlockSeventyPercentVoltageDc) * MiddleBandSlopePerVolt),
                MidpointRounding.AwayFromZero);
        }

        if (blockVoltageDc >= BlockFiftyPercentVoltageDc)
        {
            return (int)Math.Round(
                FiftyPercentSoC + ((blockVoltageDc - BlockFiftyPercentVoltageDc) * MiddleBandSlopePerVolt),
                MidpointRounding.AwayFromZero);
        }

        if (blockVoltageDc >= BlockThirtyPercentVoltageDc)
        {
            return (int)Math.Round(
                ThirtyPercentSoC + ((blockVoltageDc - BlockThirtyPercentVoltageDc) * MiddleBandSlopePerVolt),
                MidpointRounding.AwayFromZero);
        }

        if (blockVoltageDc >= BlockFifteenPercentVoltageDc)
        {
            return (int)Math.Round(
                FifteenPercentSoC + ((blockVoltageDc - BlockFifteenPercentVoltageDc) * LowerMidBandSlopePerVolt),
                MidpointRounding.AwayFromZero);
        }

        double interpolatedCutoff =
            (blockVoltageDc - BlockEmptyVoltageDc) / CutoffBandSpanVolts * FifteenPercentSoC;

        return (int)Math.Clamp(
            Math.Round(interpolatedCutoff, MidpointRounding.AwayFromZero),
            LowBatteryOverrideMinutes,
            FifteenPercentSoC);
    }
}
