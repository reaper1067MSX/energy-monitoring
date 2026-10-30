using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using Xunit;

namespace VoltTray.Tests;

public class PeukertEstimatorTests
{
    private readonly IBatteryAutonomyEstimator _estimator = new PeukertEstimator();

    [Fact]
    public void EstimateRuntimeMinutes_At17PercentLoad_DefaultCapacity_Returns25Minutes()
    {
        int minutes = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 1.0);
        Assert.Equal(25, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_At17PercentLoad_ReducedCapacity_Returns24Minutes()
    {
        int minutes = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 0.95);
        Assert.Equal(24, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_AtZeroLoad_Applies1PercentFloorAnd240MinCeiling()
    {
        int minutes0 = _estimator.EstimateRuntimeMinutes(0, capacityFactor: 1.0);
        int minutes1 = _estimator.EstimateRuntimeMinutes(1, capacityFactor: 1.0);

        Assert.Equal(240, minutes0);
        Assert.Equal(240, minutes1);
    }

    [Fact]
    public void EstimateRuntimeMinutes_AtFullLoad_ReturnsExpectedIntegerMinutes()
    {
        int minutes = _estimator.EstimateRuntimeMinutes(100, capacityFactor: 1.0);
        Assert.Equal(3, minutes);
    }

    [Fact]
    public void EstimateRuntimeMinutes_CapacityFactorClamping_ClampsToBounds()
    {
        int minClamped = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 0.10);
        int atLowerBound = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 0.30);
        Assert.Equal(atLowerBound, minClamped);

        int maxClamped = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 1.50);
        int atUpperBound = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 1.00);
        Assert.Equal(atUpperBound, maxClamped);
    }

    [Fact]
    public void EstimateRuntimeMinutes_HardwareLowBattery_OverridesTo1Minute()
    {
        int minutes = _estimator.EstimateRuntimeMinutes(17, capacityFactor: 1.0, isHardwareLow: true);
        Assert.Equal(1, minutes);

        int minutesZeroLoad = _estimator.EstimateRuntimeMinutes(0, capacityFactor: 1.0, isHardwareLow: true);
        Assert.Equal(1, minutesZeroLoad);
    }

    [Theory]
    [InlineData(27.41, 100)]
    [InlineData(27.00, 100)]
    [InlineData(26.50, 95)]
    [InlineData(25.50, 80)]
    [InlineData(24.00, 50)]
    [InlineData(23.00, 30)]
    [InlineData(22.00, 15)]
    [InlineData(20.40, 0)]
    [InlineData(19.00, 0)]
    public void CalculateSoCPercentage_MapsAgmCurveAccurately(double voltage, int expectedPct)
    {
        int actual = _estimator.CalculateSoCPercentage(voltage);
        Assert.Equal(expectedPct, actual);
    }

    [Fact]
    public void FormatRuntimeString_ProducesIntegerApproximationWithoutDecimals()
    {
        string formattedEn = _estimator.FormatRuntimeString(25, "en");
        string formattedEs = _estimator.FormatRuntimeString(25, "es");

        Assert.Equal("≈ 25 min", formattedEn);
        Assert.Equal("≈ 25 min", formattedEs);
    }

    [Fact]
    public void FormatRuntimeCaption_ProducesLocalizedCaptionWithoutDecimals()
    {
        string captionEn = _estimator.FormatRuntimeCaption(17, "en");
        string captionEs = _estimator.FormatRuntimeCaption(17, "es");

        Assert.Equal("Runtime at 17% load", captionEn);
        Assert.Equal("Autonomía a 17 % de carga", captionEs);
    }
}
