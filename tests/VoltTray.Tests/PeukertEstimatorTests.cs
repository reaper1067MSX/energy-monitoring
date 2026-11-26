using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.UI.Presenters;
using Xunit;

namespace VoltTray.Tests;

public class PeukertEstimatorTests
{
    private static readonly UpsCapabilities Default24VCapabilities = new(
        ModelName: null,
        NominalVoltageAc: 120.0,
        NominalFrequencyHz: 60.0,
        RatedWatts: 1200,
        Bank: new BatteryBankSpec(SeriesBatteryCount: 2, NominalVoltageDc: 24.0, RatedCapacityAh: 9.0),
        DialectVerified: false);

    private readonly IBatteryAutonomyEstimator _estimator = new PeukertEstimator();

    [Fact]
    public void EstimateRuntimeMinutes_At17PercentLoad_DefaultCapacity_Returns25Minutes()
    {
        int? minutes = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 1.0);
        Assert.Equal(25, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_At17PercentLoad_ReducedCapacity_Returns24Minutes()
    {
        int? minutes = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 0.95);
        Assert.Equal(24, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_AtZeroLoad_Applies1PercentFloorAnd240MinCeiling()
    {
        int? minutes0 = _estimator.EstimateRuntimeMinutes(0, Default24VCapabilities, capacityFactor: 1.0);
        int? minutes1 = _estimator.EstimateRuntimeMinutes(1, Default24VCapabilities, capacityFactor: 1.0);

        Assert.Equal(240, minutes0);
        Assert.Equal(240, minutes1);
    }

    [Fact]
    public void EstimateRuntimeMinutes_AtFullLoad_ReturnsExpectedIntegerMinutes()
    {
        int? minutes = _estimator.EstimateRuntimeMinutes(100, Default24VCapabilities, capacityFactor: 1.0);
        Assert.Equal(3, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_CapacityFactorClamping_ClampsToBounds()
    {
        int? minClamped = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 0.10);
        int? atLowerBound = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 0.30);
        Assert.Equal(atLowerBound, minClamped);

        int? maxClamped = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 1.50);
        int? atUpperBound = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 1.00);
        Assert.Equal(atUpperBound, maxClamped);
    }

    [Fact]
    public void EstimateRuntimeMinutes_HardwareLowBattery_OverridesTo1Minute()
    {
        int? minutes = _estimator.EstimateRuntimeMinutes(17, Default24VCapabilities, capacityFactor: 1.0, isHardwareLow: true);
        Assert.Equal(1, minutes);

        int? minutesZeroLoad = _estimator.EstimateRuntimeMinutes(0, Default24VCapabilities, capacityFactor: 1.0, isHardwareLow: true);
        Assert.Equal(1, minutesZeroLoad);
    }

    [Fact]
    public void EstimateRuntimeMinutes_UnconfiguredRatedWattsOrCapacity_ReturnsNull()
    {
        var unconfiguredWatts = Default24VCapabilities with { RatedWatts = null };
        var unconfiguredCapacity = Default24VCapabilities with
        {
            Bank = new BatteryBankSpec(SeriesBatteryCount: 2, NominalVoltageDc: 24.0, RatedCapacityAh: null)
        };

        Assert.Null(_estimator.EstimateRuntimeMinutes(17, unconfiguredWatts));
        Assert.Null(_estimator.EstimateRuntimeMinutes(17, unconfiguredCapacity));
        Assert.Equal(1, _estimator.EstimateRuntimeMinutes(17, unconfiguredWatts, isHardwareLow: true));
    }

    [Theory]
    [InlineData(27.41, 2, 100)]
    [InlineData(27.00, 2, 100)]
    [InlineData(26.50, 2, 95)]
    [InlineData(25.50, 2, 80)]
    [InlineData(24.00, 2, 50)]
    [InlineData(23.00, 2, 30)]
    [InlineData(22.00, 2, 15)]
    [InlineData(20.40, 2, 0)]
    [InlineData(19.00, 2, 0)]
    [InlineData(13.70, 1, 100)]
    [InlineData(12.00, 1, 50)]
    [InlineData(10.20, 1, 0)]
    [InlineData(36.00, 3, 50)]
    [InlineData(54.00, 4, 100)]
    public void CalculateSoCPercentage_MapsAgmCurveAccuratelyAcrossSeriesCounts(
        double voltage,
        int seriesBatteryCount,
        int expectedPct)
    {
        int actual = _estimator.CalculateSoCPercentage(voltage, seriesBatteryCount);
        Assert.Equal(expectedPct, actual);
    }

    [Fact]
    public void RuntimeDisplayFormatter_ProducesLocalizedStrings_AndOmitsWhenNull()
    {
        Assert.Equal("≈ 25 min", RuntimeDisplayFormatter.FormatRuntimeString(25));
        Assert.Null(RuntimeDisplayFormatter.FormatRuntimeString(null));

        Assert.Equal("Runtime at 17% load", RuntimeDisplayFormatter.FormatRuntimeCaption(25, 17, "en"));
        Assert.Equal("Autonomía a 17 % de carga", RuntimeDisplayFormatter.FormatRuntimeCaption(25, 17, "es"));
        Assert.Null(RuntimeDisplayFormatter.FormatRuntimeCaption(null, 17, "en"));
        Assert.Null(RuntimeDisplayFormatter.FormatRuntimeCaption(null, 17, "es"));
    }
}
