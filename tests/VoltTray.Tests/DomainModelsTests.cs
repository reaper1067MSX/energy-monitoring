using VoltTray.Core.Models;

namespace VoltTray.Tests;

public class DomainModelsTests
{
    [Fact]
    public void UITheme_EnumValues_AreDefined()
    {
        Assert.Equal(0, (int)UITheme.Dark);
        Assert.Equal(1, (int)UITheme.Light);
    }

    [Fact]
    public void TaskbarEdge_EnumValues_AreDefined()
    {
        Assert.Equal(0, (int)TaskbarEdge.Bottom);
        Assert.Equal(1, (int)TaskbarEdge.Top);
        Assert.Equal(2, (int)TaskbarEdge.Left);
        Assert.Equal(3, (int)TaskbarEdge.Right);
    }

    [Fact]
    public void ExitDialogResult_EnumValues_AreDefined()
    {
        Assert.Equal(0, (int)ExitDialogResult.Cancel);
        Assert.Equal(1, (int)ExitDialogResult.Pause);
        Assert.Equal(2, (int)ExitDialogResult.Close);
    }

    [Fact]
    public void ViewRect_ComputesWidthHeightAndImplementsValueEquality()
    {
        var rect = new ViewRect(10, 20, 110, 170);
        Assert.Equal(100, rect.Width);
        Assert.Equal(150, rect.Height);

        var identical = new ViewRect(10, 20, 110, 170);
        Assert.Equal(rect, identical);
        Assert.True(rect == identical);

        var different = rect with { Right = 120 };
        Assert.NotEqual(rect, different);
        Assert.Equal(110, different.Width);
    }

    [Fact]
    public void UpsOperationalState_ContainsAllSevenDistinctStates()
    {
        Assert.Equal(0, (int)UpsOperationalState.LineNormal);
        Assert.Equal(1, (int)UpsOperationalState.OnBattery);
        Assert.Equal(2, (int)UpsOperationalState.BatteryLow);
        Assert.Equal(3, (int)UpsOperationalState.ChargingAvr);
        Assert.Equal(4, (int)UpsOperationalState.Fault);
        Assert.Equal(5, (int)UpsOperationalState.Paused);
        Assert.Equal(6, (int)UpsOperationalState.CommunicationsLost);
    }

    [Fact]
    public void BatteryBankSpec_DefaultValues_ModelForzaFub1290()
    {
        var spec = new BatteryBankSpec();
        Assert.Equal(24.0, spec.NominalVoltageDc);
        Assert.Equal(9.0, spec.RatedCapacityAh);
        Assert.Equal(216.0, spec.NominalEnergyWh);
        Assert.Equal(0.036, spec.InternalResistanceOhms);
        Assert.Equal("2x Forza FUB-1290", spec.ModelName);

        var custom = spec with { RatedCapacityAh = 7.0 };
        Assert.Equal(7.0, custom.RatedCapacityAh);
        Assert.Equal(24.0, custom.NominalVoltageDc);
    }

    [Fact]
    public void UpsTelemetry_CarriesAllSixteenMetrics_AsImmutableRecord()
    {
        var now = DateTimeOffset.UtcNow;
        var bankSpec = new BatteryBankSpec();

        var telemetry = new UpsTelemetry(
            InputVoltageAc: 116.2,
            FaultReferenceVoltageAc: 116.2,
            OutputVoltageAc: 116.2,
            LoadPercentage: 14,
            ActiveLoadWatts: 126,
            FrequencyHz: 60.0,
            BatteryVoltageDc: 27.41,
            Cell1VoltageDc: 13.70,
            Cell2VoltageDc: 13.70,
            BatteryPercentage: 100,
            EstimatedRuntimeMinutes: 25,
            TemperatureCelsius: 25.0,
            State: UpsOperationalState.LineNormal,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: bankSpec,
            Timestamp: now);

        Assert.Equal(116.2, telemetry.InputVoltageAc);
        Assert.Equal(116.2, telemetry.FaultReferenceVoltageAc);
        Assert.Equal(116.2, telemetry.OutputVoltageAc);
        Assert.Equal(14, telemetry.LoadPercentage);
        Assert.Equal(126, telemetry.ActiveLoadWatts);
        Assert.Equal(60.0, telemetry.FrequencyHz);
        Assert.Equal(27.41, telemetry.BatteryVoltageDc);
        Assert.Equal(13.70, telemetry.Cell1VoltageDc);
        Assert.Equal(13.70, telemetry.Cell2VoltageDc);
        Assert.Equal(100, telemetry.BatteryPercentage);
        Assert.Equal(25, telemetry.EstimatedRuntimeMinutes);
        Assert.Equal(25.0, telemetry.TemperatureCelsius);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
        Assert.False(telemetry.IsBeeperMuted);
        Assert.False(telemetry.IsTestInProgress);
        Assert.Same(bankSpec, telemetry.BankSpec);
        Assert.Equal(now, telemetry.Timestamp);

        var modified = telemetry with { State = UpsOperationalState.OnBattery, IsBeeperMuted = true };
        Assert.Equal(UpsOperationalState.OnBattery, modified.State);
        Assert.True(modified.IsBeeperMuted);
        Assert.Equal(UpsOperationalState.LineNormal, telemetry.State);
    }
}
