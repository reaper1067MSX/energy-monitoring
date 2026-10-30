using VoltTray.Core.Models;
using VoltTray.Core.Presenters;
using VoltTray.Core.Views;

namespace VoltTray.Tests;

public class SplitCockpitPresenterTests
{
    private class MockVoltTrayView : IVoltTrayView
    {
        public bool Initialized { get; private set; }
        public UpsTelemetry? LastTelemetry { get; private set; }
        public UpsOperationalState? LastState { get; private set; }

        public void Initialize() => Initialized = true;
        public void UpdateTelemetry(UpsTelemetry telemetry) => LastTelemetry = telemetry;
        public void SetOperationalState(UpsOperationalState state) => LastState = state;
        public void Dispose() { }
    }

    private class MockTrayIconView : ITrayIconView
    {
        public UpsOperationalState? LastState { get; private set; }
        public int LastBatteryPct { get; private set; }
        public int LastDpi { get; private set; }
        public string? LastTooltip { get; private set; }
        public bool RecreateCalled { get; private set; }

        public void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi)
        {
            LastState = state;
            LastBatteryPct = batteryPct;
            LastDpi = dpi;
        }

        public void SetTooltip(string text) => LastTooltip = text;
        public void Recreate() => RecreateCalled = true;
        public void Dispose() { }
    }

    private class MockPopoverView : IPopoverView
    {
        public bool IsVisible { get; set; }
        public UpsTelemetry? LastTelemetry { get; private set; }
        public UITheme? LastTheme { get; private set; }
        public ViewRect? LastIconRect { get; private set; }
        public TaskbarEdge? LastEdge { get; private set; }
        public ViewRect? LastWorkArea { get; private set; }
        public bool HideCalled { get; private set; }

        public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea)
        {
            IsVisible = true;
            LastTelemetry = telemetry;
            LastTheme = theme;
            LastIconRect = iconRect;
            LastEdge = edge;
            LastWorkArea = workArea;
        }

        public void Hide()
        {
            IsVisible = false;
            HideCalled = true;
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData(27.41, 13.70, 13.70)]
    [InlineData(24.00, 12.00, 12.00)]
    [InlineData(25.55, 12.78, 12.78)]
    [InlineData(0.00, 0.00, 0.00)]
    public void DeriveSymmetricCells_CalculatesHalfVoltageToTwoDecimals(double totalVoltage, double expectedCell1, double expectedCell2)
    {
        var (c1, c2) = SplitCockpitPresenter.DeriveSymmetricCells(totalVoltage);
        Assert.Equal(expectedCell1, c1);
        Assert.Equal(expectedCell2, c2);
    }

    [Theory]
    [InlineData(14, 900, 126)]
    [InlineData(0, 900, 0)]
    [InlineData(100, 900, 900)]
    [InlineData(50, 900, 450)]
    public void CalculateActiveLoadWatts_CalculatesFromWorkstationRatedCapacity(int loadPct, int ratedWatts, int expectedWatts)
    {
        int actualWatts = SplitCockpitPresenter.CalculateActiveLoadWatts(loadPct, ratedWatts);
        Assert.Equal(expectedWatts, actualWatts);
    }

    [Fact]
    public void CommandConstants_MatchProtocolRequirements()
    {
        Assert.Equal("Q\r", SplitCockpitPresenter.BeeperToggleCommand);
        Assert.Equal("T\r", SplitCockpitPresenter.SelfTestCommand);
    }

    [Theory]
    [InlineData(UpsOperationalState.LineNormal, 100, 25, "Forza FX-2200 LCD · Online (100% · ≈ 25 min)")]
    [InlineData(UpsOperationalState.OnBattery, 82, 19, "Forza FX-2200 LCD · On Battery (82% · ≈ 19 min)")]
    [InlineData(UpsOperationalState.BatteryLow, 15, 3, "Forza FX-2200 LCD · Battery Low (15% · ≈ 3 min)")]
    [InlineData(UpsOperationalState.ChargingAvr, 100, 25, "Forza FX-2200 LCD · AVR Boost (100% · ≈ 25 min)")]
    [InlineData(UpsOperationalState.Fault, 0, 0, "Forza FX-2200 LCD · Fault (Check Unit)")]
    [InlineData(UpsOperationalState.Paused, 100, 25, "Forza FX-2200 LCD · Monitoring Paused")]
    [InlineData(UpsOperationalState.CommunicationsLost, 0, 0, "Forza FX-2200 LCD · No Communication")]
    public void FormatTooltip_GeneratesFormattedString(UpsOperationalState state, int battPct, int runtimeMin, string expected)
    {
        string actual = SplitCockpitPresenter.FormatTooltip(state, battPct, runtimeMin);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UpdateTelemetry_UpdatesViewsAndGeneratesTooltip()
    {
        var mockView = new MockVoltTrayView();
        var mockTray = new MockTrayIconView();
        var mockPopover = new MockPopoverView();
        string? dispatchedCommand = null;

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: cmd => { dispatchedCommand = cmd; return true; });

        presenter.Initialize();
        Assert.True(mockView.Initialized);

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
            BankSpec: new BatteryBankSpec(),
            Timestamp: DateTimeOffset.UtcNow);

        presenter.UpdateTelemetry(telemetry, dpi: 144);

        Assert.Same(telemetry, mockView.LastTelemetry);
        Assert.Equal(UpsOperationalState.LineNormal, mockView.LastState);
        Assert.Equal(UpsOperationalState.LineNormal, mockTray.LastState);
        Assert.Equal(100, mockTray.LastBatteryPct);
        Assert.Equal(144, mockTray.LastDpi);
        Assert.Equal("Forza FX-2200 LCD · Online (100% · ≈ 25 min)", mockTray.LastTooltip);
        Assert.Same(telemetry, presenter.CurrentTelemetry);

        // Action commands test
        bool beeperSuccess = presenter.ToggleBeeper();
        Assert.True(beeperSuccess);
        Assert.Equal("Q\r", dispatchedCommand);

        bool testSuccess = presenter.TriggerSelfTest();
        Assert.True(testSuccess);
        Assert.Equal("T\r", dispatchedCommand);
    }

    [Fact]
    public void TogglePopover_ShowsWhenHidden_AndHidesWhenVisible()
    {
        var mockView = new MockVoltTrayView();
        var mockTray = new MockTrayIconView();
        var mockPopover = new MockPopoverView();

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: _ => true);

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
            BankSpec: new BatteryBankSpec(),
            Timestamp: DateTimeOffset.UtcNow);

        presenter.UpdateTelemetry(telemetry);

        var iconRect = new ViewRect(1800, 1030, 1840, 1070);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        // Initially hidden -> Show
        Assert.False(mockPopover.IsVisible);
        presenter.TogglePopover(iconRect, TaskbarEdge.Bottom, workArea, UITheme.Dark);
        Assert.True(mockPopover.IsVisible);
        Assert.Same(telemetry, mockPopover.LastTelemetry);
        Assert.Equal(UITheme.Dark, mockPopover.LastTheme);

        // When visible -> Hide
        presenter.TogglePopover(iconRect, TaskbarEdge.Bottom, workArea, UITheme.Dark);
        Assert.False(mockPopover.IsVisible);
        Assert.True(mockPopover.HideCalled);
    }
}
