using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using VoltTray.Core.Telemetry;
using VoltTray.Tests.ObjectMothers;
using VoltTray.UI.Models;

namespace VoltTray.Tests;

public class SplitCockpitPresenterTests
{
    private class MockTrayIconView : ITrayIconView
    {
        public event Action? OnTrayIconClicked { add { } remove { } }
        public event Action? OnContextMenuRequested { add { } remove { } }

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
        public ViewRect? TryGetIconRect() => null;
        public void Dispose() { }
    }

    private class MockPopoverView : IPopoverView
    {
        public event Action? OnSelfTestRequested { add { } remove { } }
        public event Action? OnToggleBeeperRequested { add { } remove { } }

        public bool IsVisible { get; set; }
        public UITheme CurrentTheme { get; private set; } = UITheme.Dark;
        public UpsTelemetry? LastTelemetry { get; private set; }
        public UITheme? LastTheme { get; private set; }
        public ViewRect? LastIconRect { get; private set; }
        public TaskbarEdge? LastEdge { get; private set; }
        public ViewRect? LastWorkArea { get; private set; }
        public bool HideCalled { get; private set; }

        public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea)
        {
            IsVisible = true;
            CurrentTheme = theme;
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
    public async Task ActionButtons_InvokeDomainCallbacks_WithoutWireStrings()
    {
        int beeperCalls = 0;
        int selfTestCalls = 0;

        var presenter = new SplitCockpitPresenter(
            trayIcon: new MockTrayIconView(),
            popover: new MockPopoverView(),
            toggleBeeper: () => { beeperCalls++; return Task.FromResult(true); },
            triggerSelfTest: () => { selfTestCalls++; return Task.FromResult(false); });

        Assert.True(await presenter.ToggleBeeperAsync());
        Assert.False(await presenter.TriggerSelfTestAsync());
        Assert.Equal(1, beeperCalls);
        Assert.Equal(1, selfTestCalls);
    }

    [Theory]
    [InlineData(UpsOperationalState.LineNormal, 100, 25, null, "VoltTray UPS · Online (100% · ≈ 25 min)")]
    [InlineData(UpsOperationalState.LineNormal, 100, null, null, "VoltTray UPS · Online (100%)")]
    [InlineData(UpsOperationalState.LineNormal, 100, 25, "Forza NT-2401PRO", "Forza NT-2401PRO · Online (100% · ≈ 25 min)")]
    [InlineData(UpsOperationalState.OnBattery, 82, 19, null, "VoltTray UPS · On Battery (82% · ≈ 19 min)")]
    [InlineData(UpsOperationalState.BatteryLow, 15, 3, null, "VoltTray UPS · Battery Low (15% · ≈ 3 min)")]
    [InlineData(UpsOperationalState.ChargingAvr, 100, 25, null, "VoltTray UPS · AVR Boost (100% · ≈ 25 min)")]
    [InlineData(UpsOperationalState.Fault, 0, 0, null, "VoltTray UPS · Fault (Check Unit)")]
    [InlineData(UpsOperationalState.Paused, 100, 25, null, "VoltTray UPS · Monitoring Paused")]
    [InlineData(UpsOperationalState.CommunicationsLost, 0, 0, null, "VoltTray UPS · No Communication")]
    public void FormatTooltip_GeneratesFormattedString(
        UpsOperationalState state,
        int battPct,
        int? runtimeMin,
        string? modelName,
        string expected)
    {
        string actual = SplitCockpitPresenter.FormatTooltip(state, battPct, runtimeMin, modelName);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task UpdateTelemetry_UpdatesViewsAndGeneratesTooltip()
    {
        var mockTray = new MockTrayIconView();
        var mockPopover = new MockPopoverView();
        bool beeperTriggered = false;
        bool selfTestTriggered = false;

        var presenter = new SplitCockpitPresenter(
            trayIcon: mockTray,
            popover: mockPopover,
            toggleBeeper: () => { beeperTriggered = true; return Task.FromResult(true); },
            triggerSelfTest: () => { selfTestTriggered = true; return Task.FromResult(true); });

        var telemetry = TelemetryMother.CreateLineNormal(inV: 116.2, outV: 116.2, runtimeMinutes: 25);

        presenter.UpdateTelemetry(telemetry, dpi: 144);

        Assert.Equal(UpsOperationalState.LineNormal, mockTray.LastState);
        Assert.Equal(100, mockTray.LastBatteryPct);
        Assert.Equal(144, mockTray.LastDpi);
        Assert.Equal("VoltTray UPS · Online (100% · ≈ 25 min)", mockTray.LastTooltip);
        Assert.Same(telemetry, presenter.CurrentTelemetry);

        // Action commands test
        bool beeperSuccess = await presenter.ToggleBeeperAsync();
        Assert.True(beeperSuccess);
        Assert.True(beeperTriggered);

        bool testSuccess = await presenter.TriggerSelfTestAsync();
        Assert.True(testSuccess);
        Assert.True(selfTestTriggered);
    }

    [Fact]
    public void TogglePopover_ShowsWhenHidden_AndHidesWhenVisible()
    {
        var mockTray = new MockTrayIconView();
        var mockPopover = new MockPopoverView();

        var presenter = new SplitCockpitPresenter(
            trayIcon: mockTray,
            popover: mockPopover,
            toggleBeeper: () => Task.FromResult(true),
            triggerSelfTest: () => Task.FromResult(true));

        var telemetry = TelemetryMother.CreateLineNormal(inV: 116.2, outV: 116.2, runtimeMinutes: 25);

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
