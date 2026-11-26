using VoltTray.Core.Models;

namespace VoltTray.Core.Services;

public sealed class BatteryBankInference
{
    public const int RequiredConsecutiveReadings = 3;
    public const int NominalVoltagePerBlock = 12;

    private const double Min12VBand = 9.5;
    private const double Max12VBand = 15.5;
    private const double Min24VBand = 19.0;
    private const double Max24VBand = 31.0;
    private const double Min36VBand = 31.1;
    private const double Max36VBand = 46.5;
    private const double Min48VBand = 46.6;
    private const double Max48VBand = 62.0;

    private const int SeriesCount12V = 1;
    private const int SeriesCount24V = 2;
    private const int SeriesCount36V = 3;
    private const int SeriesCount48V = 4;

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
            int hardwareSeriesCount = hardwareNominalVoltageDc.Value / NominalVoltagePerBlock;
            var hardwareBank = new BatteryBankSpec(
                SeriesBatteryCount: hardwareSeriesCount,
                NominalVoltageDc: hardwareNominalVoltageDc.Value,
                RatedCapacityAh: ratedCapacityAh,
                Label: label);

            LatchedBank = hardwareBank;
            PendingSeriesCount = hardwareSeriesCount;
            ConsecutiveMatchCount = RequiredConsecutiveReadings;
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

        if (ConsecutiveMatchCount >= RequiredConsecutiveReadings)
        {
            LatchedBank = new BatteryBankSpec(
                SeriesBatteryCount: detectedSeriesCount.Value,
                NominalVoltageDc: detectedSeriesCount.Value * NominalVoltagePerBlock,
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

        if (measuredVoltageDc >= Min12VBand && measuredVoltageDc <= Max12VBand)
        {
            return SeriesCount12V;
        }

        if (measuredVoltageDc >= Min24VBand && measuredVoltageDc <= Max24VBand)
        {
            return SeriesCount24V;
        }

        if (measuredVoltageDc >= Min36VBand && measuredVoltageDc <= Max36VBand)
        {
            return SeriesCount36V;
        }

        if (measuredVoltageDc >= Min48VBand && measuredVoltageDc <= Max48VBand)
        {
            return SeriesCount48V;
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
