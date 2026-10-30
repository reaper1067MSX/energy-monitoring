using System.Globalization;
using VoltTray.Core.Models;
using VoltTray.Core.Presenters;
using VoltTray.Core.Tokens;
using VoltTray.Core.Views;

namespace VoltTray.Tests;

public class Phase1AdversarialTests
{
    private class TrackingVoltTrayView : IVoltTrayView
    {
        public int InitializeCount { get; private set; }
        public int UpdateTelemetryCount { get; private set; }
        public int SetOperationalStateCount { get; private set; }
        public UpsTelemetry? LastTelemetry { get; private set; }
        public UpsOperationalState? LastState { get; private set; }

        public void Initialize() => InitializeCount++;
        public void UpdateTelemetry(UpsTelemetry telemetry)
        {
            UpdateTelemetryCount++;
            LastTelemetry = telemetry;
        }
        public void SetOperationalState(UpsOperationalState state)
        {
            SetOperationalStateCount++;
            LastState = state;
        }
        public void Dispose() { }
    }

    private class TrackingTrayIconView : ITrayIconView
    {
        public int UpdateIconCount { get; private set; }
        public int SetTooltipCount { get; private set; }
        public UpsOperationalState? LastState { get; private set; }
        public int LastBatteryPct { get; private set; }
        public int LastDpi { get; private set; }
        public string? LastTooltip { get; private set; }
        public void UpdateIcon(UpsOperationalState state, int batteryPct, int dpi)
        {
            UpdateIconCount++;
            LastState = state;
            LastBatteryPct = batteryPct;
            LastDpi = dpi;
        }
        public void SetTooltip(string text)
        {
            SetTooltipCount++;
            LastTooltip = text;
        }
        public void Recreate() { }
        public void Dispose() { }
    }

    private class TrackingPopoverView : IPopoverView
    {
        public bool IsVisible { get; set; }
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public UpsTelemetry? LastTelemetry { get; private set; }
        public UITheme? LastTheme { get; private set; }
        public ViewRect? LastIconRect { get; private set; }
        public TaskbarEdge? LastEdge { get; private set; }
        public ViewRect? LastWorkArea { get; private set; }

        public void Show(UpsTelemetry telemetry, UITheme theme, ViewRect iconRect, TaskbarEdge edge, ViewRect workArea)
        {
            IsVisible = true;
            ShowCount++;
            LastTelemetry = telemetry;
            LastTheme = theme;
            LastIconRect = iconRect;
            LastEdge = edge;
            LastWorkArea = workArea;
        }

        public void Hide()
        {
            IsVisible = false;
            HideCount++;
        }

        public void Dispose() { }
    }

    private static UpsTelemetry CreateTelemetry(
        double vbank = 27.41,
        int loadPct = 14,
        UpsOperationalState state = UpsOperationalState.LineNormal) =>
        new(
            InputVoltageAc: 116.2,
            FaultReferenceVoltageAc: 116.2,
            OutputVoltageAc: 116.2,
            LoadPercentage: loadPct,
            ActiveLoadWatts: SplitCockpitPresenter.CalculateActiveLoadWatts(loadPct),
            FrequencyHz: 60.0,
            BatteryVoltageDc: vbank,
            Cell1VoltageDc: SplitCockpitPresenter.DeriveSymmetricCells(vbank).Cell1,
            Cell2VoltageDc: SplitCockpitPresenter.DeriveSymmetricCells(vbank).Cell2,
            BatteryPercentage: 100,
            EstimatedRuntimeMinutes: 25,
            TemperatureCelsius: 25.0,
            State: state,
            IsBeeperMuted: false,
            IsTestInProgress: false,
            BankSpec: new BatteryBankSpec(),
            Timestamp: DateTimeOffset.UtcNow);

    // =========================================================================
    // 1. Contrast Ratio Calculations on Extreme Hex Colors
    // =========================================================================

    [Fact]
    public void ContrastRatio_IdenticalColors_AlwaysYieldsExactlyOneToOne()
    {
        string[] colors = ["#000000", "#FFFFFF", "#123456", "#FFB84D", "#2B2B2B", "#FAFAFA", "#6CCB5F"];

        foreach (var color in colors)
        {
            double ratio = DesignTokens.CalculateContrastRatio(color, color);
            Assert.Equal(1.0, ratio, precision: 5);
        }
    }

    [Fact]
    public void ContrastRatio_ExtremeLuminance_BlackAndWhiteYieldsTwentyOneToOne()
    {
        double bw = DesignTokens.CalculateContrastRatio("#000000", "#FFFFFF");
        double wb = DesignTokens.CalculateContrastRatio("#FFFFFF", "#000000");

        Assert.Equal(21.0, bw, precision: 2);
        Assert.Equal(21.0, wb, precision: 2);
    }

    [Fact]
    public void ContrastRatio_SymmetryProperty_OrderDoesNotAffectResult()
    {
        (string, string)[] pairs =
        [
            ("#1A1A1A", "#FAFAFA"),
            ("#60CDFF", "#0A1A22"),
            ("#B3261E", "#EEEEEE"),
            ("#7BE070", "#1D3A22")
        ];

        foreach (var (c1, c2) in pairs)
        {
            double r1 = DesignTokens.CalculateContrastRatio(c1, c2);
            double r2 = DesignTokens.CalculateContrastRatio(c2, c1);
            Assert.Equal(r1, r2, precision: 5);
        }
    }

    [Fact]
    public void ContrastRatio_CaseInsensitiveAndPrefixTolerant()
    {
        double r1 = DesignTokens.CalculateContrastRatio("#ffffff", "#000000");
        double r2 = DesignTokens.CalculateContrastRatio("FFFFFF", "000000");
        double r3 = DesignTokens.CalculateContrastRatio("#FfFfFf", "#000000");

        Assert.Equal(21.0, r1, precision: 2);
        Assert.Equal(21.0, r2, precision: 2);
        Assert.Equal(21.0, r3, precision: 2);
    }

    [Fact]
    public void ContrastRatio_RangeBoundsInvariant_AlwaysBetweenOneAndTwentyOne()
    {
        // Randomized grid probe across RGB channels
        int[] channelSamples = [0, 16, 64, 128, 192, 255];
        foreach (int r1 in channelSamples)
        foreach (int g1 in channelSamples)
        foreach (int b1 in channelSamples)
        {
            string c1 = $"#{r1:X2}{g1:X2}{b1:X2}";
            string c2 = "#2B2B2B"; // Dark surface

            double ratio = DesignTokens.CalculateContrastRatio(c1, c2);
            Assert.InRange(ratio, 1.0, 21.0);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#FFF")] // 3-digit hex not supported
    [InlineData("not-a-color")]
    public void ContrastRatio_InvalidHexFormat_ThrowsExceptionSafely(string invalidHex)
    {
        Assert.ThrowsAny<Exception>(() => DesignTokens.CalculateContrastRatio(invalidHex, "#FFFFFF"));
    }

    // =========================================================================
    // 2. Presenter Handling of Abnormal Telemetry
    // =========================================================================

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    [InlineData(-9999.99)]
    public void DeriveSymmetricCells_NonPositiveVoltage_ReturnsZeroZero(double nonPositiveVbank)
    {
        var (c1, c2) = SplitCockpitPresenter.DeriveSymmetricCells(nonPositiveVbank);
        Assert.Equal(0.0, c1);
        Assert.Equal(0.0, c2);
    }

    [Theory]
    [InlineData(100.0, 50.0)]
    [InlineData(500.0, 250.0)]
    [InlineData(1000.0, 500.0)]
    [InlineData(2500.0, 1250.0)]
    public void DeriveSymmetricCells_ExtremePositiveVoltage_ComputesHalfSafely(double vbank, double expectedHalf)
    {
        var (c1, c2) = SplitCockpitPresenter.DeriveSymmetricCells(vbank);
        Assert.Equal(expectedHalf, c1);
        Assert.Equal(expectedHalf, c2);
    }

    [Theory]
    [InlineData(-100, 900, 0)]
    [InlineData(-10, 900, 0)]
    [InlineData(-1, 900, 0)]
    [InlineData(0, 900, 0)]
    [InlineData(1, 900, 9)]
    [InlineData(14, 900, 126)]
    [InlineData(50, 900, 450)]
    [InlineData(100, 900, 900)]
    [InlineData(101, 900, 900)]
    [InlineData(150, 900, 900)]
    [InlineData(9999, 900, 900)]
    [InlineData(int.MinValue, 900, 0)]
    [InlineData(int.MaxValue, 900, 900)]
    public void CalculateActiveLoadWatts_BoundaryAndExtremeClamping(int loadPct, int ratedWatts, int expectedWatts)
    {
        int actual = SplitCockpitPresenter.CalculateActiveLoadWatts(loadPct, ratedWatts);
        Assert.Equal(expectedWatts, actual);
    }

    // =========================================================================
    // 3. Symmetric Cell Derivation Numerical Stability
    // =========================================================================

    [Fact]
    public void DeriveSymmetricCells_SweepExhaustiveStability_0To35VoltsAtOneCentivoltResolution()
    {
        // 3500 distinct voltage levels tested for floating point drift, symmetry, and error bounds
        for (int centivolts = 0; centivolts <= 3500; centivolts++)
        {
            double v = centivolts / 100.0;
            var (c1, c2) = SplitCockpitPresenter.DeriveSymmetricCells(v);

            // Invariant 1: Series cells must be strictly equal (symmetric derivation)
            Assert.Equal(c1, c2);

            if (v == 0.0)
            {
                Assert.Equal(0.0, c1);
                continue;
            }

            // Invariant 2: Derived half voltage must be within 0.01V of exact mathematical v/2
            double exactHalf = v / 2.0;
            double error = Math.Abs(c1 - exactHalf);
            Assert.True(error <= 0.010000001, $"Floating point drift at {v}V: c1={c1}, exactHalf={exactHalf}, error={error}");

            // Invariant 3: Total sum (c1 + c2) reconstructed must differ by at most 0.01V from original v
            double reconstructedTotal = c1 + c2;
            double totalError = Math.Abs(reconstructedTotal - v);
            Assert.True(totalError <= 0.010000001, $"Reconstruction error at {v}V: reconstructed={reconstructedTotal}, original={v}");

            // Invariant 4: No negative zero (-0.0)
            Assert.False(double.IsNegative(c1));
        }
    }

    // =========================================================================
    // 4. Action Dock Command Generation Idempotency
    // =========================================================================

    [Fact]
    public void ActionDockCommands_IdempotencyAndStatePurity()
    {
        var mockView = new TrackingVoltTrayView();
        var mockTray = new TrackingTrayIconView();
        var mockPopover = new TrackingPopoverView();

        var dispatchedCommands = new List<string>();
        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: cmd =>
            {
                dispatchedCommands.Add(cmd);
                return true;
            });

        var telemetry = CreateTelemetry();
        presenter.UpdateTelemetry(telemetry);

        // Repeated Beeper Toggle invocations
        for (int i = 0; i < 50; i++)
        {
            bool ok = presenter.ToggleBeeper();
            Assert.True(ok);
        }

        // Repeated Self Test invocations
        for (int i = 0; i < 50; i++)
        {
            bool ok = presenter.TriggerSelfTest();
            Assert.True(ok);
        }

        // Verify exact command tokens generated
        Assert.Equal(100, dispatchedCommands.Count);
        Assert.All(dispatchedCommands.Take(50), cmd => Assert.Equal("Q\r", cmd));
        Assert.All(dispatchedCommands.Skip(50), cmd => Assert.Equal("T\r", cmd));

        // State purity: presenter telemetry and view references remain unaffected by command dispatches
        Assert.Same(telemetry, presenter.CurrentTelemetry);
        Assert.Equal(1, mockView.UpdateTelemetryCount);
    }

    [Fact]
    public void ActionDockCommands_PropagateFalseOnCommunicationFailure()
    {
        var mockView = new TrackingVoltTrayView();
        var mockTray = new TrackingTrayIconView();
        var mockPopover = new TrackingPopoverView();

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: _ => false); // Failure simulation

        Assert.False(presenter.ToggleBeeper());
        Assert.False(presenter.TriggerSelfTest());
    }

    // =========================================================================
    // 5. View Interface Implementations and Event Callbacks
    // =========================================================================

    [Fact]
    public void Constructor_ThrowsArgumentNullException_OnAnyNullDependency()
    {
        var view = new TrackingVoltTrayView();
        var tray = new TrackingTrayIconView();
        var popover = new TrackingPopoverView();
        Func<string, bool> send = _ => true;

        Assert.Throws<ArgumentNullException>("view", () => new SplitCockpitPresenter(null!, tray, popover, send));
        Assert.Throws<ArgumentNullException>("trayIcon", () => new SplitCockpitPresenter(view, null!, popover, send));
        Assert.Throws<ArgumentNullException>("popover", () => new SplitCockpitPresenter(view, tray, null!, send));
        Assert.Throws<ArgumentNullException>("sendCommand", () => new SplitCockpitPresenter(view, tray, popover, null!));
    }

    [Fact]
    public void Presenter_RapidStateTransitions_ConsistentlyUpdatesViewsAndColors()
    {
        var mockView = new TrackingVoltTrayView();
        var mockTray = new TrackingTrayIconView();
        var mockPopover = new TrackingPopoverView();

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: _ => true);

        presenter.Initialize();
        Assert.Equal(1, mockView.InitializeCount);

        UpsOperationalState[] statesSequence =
        [
            UpsOperationalState.LineNormal,
            UpsOperationalState.ChargingAvr,
            UpsOperationalState.OnBattery,
            UpsOperationalState.BatteryLow,
            UpsOperationalState.Fault,
            UpsOperationalState.Paused,
            UpsOperationalState.CommunicationsLost,
            UpsOperationalState.LineNormal
        ];

        for (int i = 0; i < statesSequence.Length; i++)
        {
            var state = statesSequence[i];
            var telemetry = CreateTelemetry(state: state);
            presenter.UpdateTelemetry(telemetry, dpi: 96 + i * 24);

            Assert.Same(telemetry, mockView.LastTelemetry);
            Assert.Equal(state, mockView.LastState);
            Assert.Equal(state, mockTray.LastState);
            Assert.Equal(96 + i * 24, mockTray.LastDpi);

            // Check tooltip is not null or empty
            Assert.False(string.IsNullOrWhiteSpace(mockTray.LastTooltip));

            // Check taskbar colors for state exist and are valid hex colors
            string darkColor = DesignTokens.GetTrayStateColor(state, isLightTaskbar: false);
            string lightColor = DesignTokens.GetTrayStateColor(state, isLightTaskbar: true);

            Assert.StartsWith("#", darkColor);
            Assert.StartsWith("#", lightColor);
            Assert.Equal(7, darkColor.Length);
            Assert.Equal(7, lightColor.Length);
        }

        Assert.Equal(statesSequence.Length, mockView.UpdateTelemetryCount);
        Assert.Equal(statesSequence.Length, mockTray.UpdateIconCount);
    }

    [Fact]
    public void TogglePopover_WithoutTelemetry_DoesNotShow()
    {
        var mockView = new TrackingVoltTrayView();
        var mockTray = new TrackingTrayIconView();
        var mockPopover = new TrackingPopoverView();

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: _ => true);

        var rect = new ViewRect(0, 0, 100, 100);
        presenter.TogglePopover(rect, TaskbarEdge.Bottom, rect, UITheme.Dark);

        Assert.False(mockPopover.IsVisible);
        Assert.Equal(0, mockPopover.ShowCount);
        Assert.Equal(0, mockPopover.HideCount);
    }

    [Fact]
    public void TogglePopover_MultiCycleTransitions_HidesAndShowsCleanly()
    {
        var mockView = new TrackingVoltTrayView();
        var mockTray = new TrackingTrayIconView();
        var mockPopover = new TrackingPopoverView();

        var presenter = new SplitCockpitPresenter(
            view: mockView,
            trayIcon: mockTray,
            popover: mockPopover,
            sendCommand: _ => true);

        var telemetry = CreateTelemetry();
        presenter.UpdateTelemetry(telemetry);

        var iconRect = new ViewRect(1800, 1030, 1840, 1070);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        // 10 cycles of open / close
        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Toggle open
            presenter.TogglePopover(iconRect, TaskbarEdge.Bottom, workArea, UITheme.Dark);
            Assert.True(mockPopover.IsVisible);
            Assert.Equal(cycle + 1, mockPopover.ShowCount);

            // Toggle close
            presenter.TogglePopover(iconRect, TaskbarEdge.Bottom, workArea, UITheme.Dark);
            Assert.False(mockPopover.IsVisible);
            Assert.Equal(cycle + 1, mockPopover.HideCount);
        }
    }
}
