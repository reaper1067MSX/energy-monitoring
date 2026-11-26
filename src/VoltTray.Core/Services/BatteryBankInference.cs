using VoltTray.Core.Constants;
using VoltTray.Core.Models;
using VoltTray.Shared;

namespace VoltTray.Core.Services;

public sealed class BatteryBankInference
{
    private static readonly BatteryVoltageBand[] SupportedBands =
    [
        new(SeriesCount: 1, MinVoltageDc: 9.5, MaxVoltageDc: 15.5),
        new(SeriesCount: 2, MinVoltageDc: 19.0, MaxVoltageDc: 31.0),
        new(SeriesCount: 3, MinVoltageDc: 31.1, MaxVoltageDc: 46.5),
        new(SeriesCount: 4, MinVoltageDc: 46.6, MaxVoltageDc: 62.0)
    ];

    public BatteryBankSpec? LatchedBank { get; private set; }
    public int? PendingSeriesCount { get; private set; }
    public int ConsecutiveMatchCount { get; private set; }

    public BatteryBankSpec? Resolve(
        double measuredVoltageDc,
        int? hardwareNominalVoltageDc = null,
        double? ratedCapacityAh = null,
        string? label = null)
    {
        if (hardwareNominalVoltageDc is 12 or 24 or 36 or 48)
        {
            int hardwareSeriesCount = hardwareNominalVoltageDc.Value / ElectricalConstants.NominalBlockVoltageInt;
            var hardwareBank = new BatteryBankSpec(
                SeriesBatteryCount: hardwareSeriesCount,
                NominalVoltageDc: hardwareNominalVoltageDc.Value,
                RatedCapacityAh: ratedCapacityAh,
                Label: label);

            LatchedBank = hardwareBank;
            PendingSeriesCount = hardwareSeriesCount;
            ConsecutiveMatchCount = BatteryConstants.RequiredConsecutiveReadings;
            return hardwareBank;
        }

        int? detectedSeriesCount = ClassifySeriesCount(measuredVoltageDc);
        if (detectedSeriesCount is null)
        {
            PendingSeriesCount = null;
            ConsecutiveMatchCount = 0;
            return LatchedBank;
        }

        if (PendingSeriesCount == detectedSeriesCount)
        {
            ConsecutiveMatchCount++;
        }
        else
        {
            PendingSeriesCount = detectedSeriesCount;
            ConsecutiveMatchCount = 1;
        }

        if (ConsecutiveMatchCount >= BatteryConstants.RequiredConsecutiveReadings)
        {
            LatchedBank = new BatteryBankSpec(
                SeriesBatteryCount: detectedSeriesCount.Value,
                NominalVoltageDc: detectedSeriesCount.Value * ElectricalConstants.NominalBlockVoltageInt,
                RatedCapacityAh: ratedCapacityAh,
                Label: label);
        }

        return LatchedBank;
    }

    public static int? ClassifySeriesCount(double measuredVoltageDc)
    {
        if (double.IsNaN(measuredVoltageDc))
        {
            return null;
        }

        foreach (BatteryVoltageBand band in SupportedBands)
        {
            if (measuredVoltageDc >= band.MinVoltageDc && measuredVoltageDc <= band.MaxVoltageDc)
            {
                return band.SeriesCount;
            }
        }

        return null;
    }

    public void Reset()
    {
        LatchedBank = null;
        PendingSeriesCount = null;
        ConsecutiveMatchCount = 0;
    }
}
