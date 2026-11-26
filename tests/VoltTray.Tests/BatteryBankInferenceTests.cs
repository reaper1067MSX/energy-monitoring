using VoltTray.Core.Battery;

namespace VoltTray.Tests;

public class BatteryBankInferenceTests
{
    [Theory]
    [InlineData(12, 1, 12.0)]
    [InlineData(24, 2, 24.0)]
    [InlineData(36, 3, 36.0)]
    [InlineData(48, 4, 48.0)]
    public void Resolve_HardwareNominalVoltage_TakesImmediatePrecedenceOnFirstReading(
        int hardwareNominalVoltageDc,
        int expectedSeriesCount,
        double expectedNominalVoltageDc)
    {
        var inference = new BatteryBankInference();

        BatteryBankSpec? resolved = inference.Resolve(
            measuredVoltageDc: 13.70,
            hardwareNominalVoltageDc: hardwareNominalVoltageDc,
            ratedCapacityAh: 9.0,
            label: "Hardware Reported");

        Assert.NotNull(resolved);
        Assert.Equal(expectedSeriesCount, resolved.SeriesBatteryCount);
        Assert.Equal(expectedNominalVoltageDc, resolved.NominalVoltageDc);
        Assert.Equal(9.0, resolved.RatedCapacityAh);
        Assert.Equal("Hardware Reported", resolved.Label);
        Assert.Equal(resolved, inference.LatchedBank);
    }

    [Theory]
    [InlineData(9.5, 1, 12.0)]
    [InlineData(13.70, 1, 12.0)]
    [InlineData(15.5, 1, 12.0)]
    [InlineData(19.0, 2, 24.0)]
    [InlineData(27.41, 2, 24.0)]
    [InlineData(31.0, 2, 24.0)]
    [InlineData(31.1, 3, 36.0)]
    [InlineData(40.5, 3, 36.0)]
    [InlineData(46.5, 3, 36.0)]
    [InlineData(46.6, 4, 48.0)]
    [InlineData(54.8, 4, 48.0)]
    [InlineData(62.0, 4, 48.0)]
    public void Resolve_FallbackVoltageBands_LatchStrictlyAfterThreeConsistentReadings(
        double measuredVoltageDc,
        int expectedSeriesCount,
        double expectedNominalVoltageDc)
    {
        var inference = new BatteryBankInference();

        Assert.Null(inference.Resolve(measuredVoltageDc));
        Assert.Equal(1, inference.ConsecutiveMatchCount);

        Assert.Null(inference.Resolve(measuredVoltageDc));
        Assert.Equal(2, inference.ConsecutiveMatchCount);

        BatteryBankSpec? latched = inference.Resolve(measuredVoltageDc);
        Assert.NotNull(latched);
        Assert.Equal(expectedSeriesCount, latched.SeriesBatteryCount);
        Assert.Equal(expectedNominalVoltageDc, latched.NominalVoltageDc);
    }

    [Fact]
    public void Resolve_FluctuatingOrOutOfBandReadings_ResetConsistencyStreak()
    {
        var inference = new BatteryBankInference();

        Assert.Null(inference.Resolve(13.70));
        Assert.Null(inference.Resolve(13.70));
        Assert.Equal(2, inference.ConsecutiveMatchCount);

        // Interleaved gap voltage resets streak
        Assert.Null(inference.Resolve(17.20));
        Assert.Equal(0, inference.ConsecutiveMatchCount);
        Assert.Null(inference.LatchedBank);

        // Switching to 24V band requires 3 fresh readings
        Assert.Null(inference.Resolve(27.40));
        Assert.Null(inference.Resolve(27.40));
        BatteryBankSpec? latched24 = inference.Resolve(27.40);

        Assert.NotNull(latched24);
        Assert.Equal(2, latched24.SeriesBatteryCount);
        Assert.Equal(24.0, latched24.NominalVoltageDc);
    }
}
