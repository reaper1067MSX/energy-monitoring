using System.Globalization;
using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Models;
using VoltTray.UI.Presenters;
using VoltTray.UI.Rendering;
using VoltTray.UI.Tokens;

namespace VoltTray.Tests;

public class UIRenderersTests
{
    [Fact]
    public void TrayRenderer_UpdatesAllOperationalStates_WithoutLeak()
    {
        using var renderer = new TrayRenderer();

        renderer.UpdateIcon(UpsOperationalState.LineNormal, 100, dpi: 96);
        Assert.NotEqual(IntPtr.Zero, renderer.CurrentHIcon);

        renderer.UpdateIcon(UpsOperationalState.OnBattery, 73, dpi: 120);
        Assert.NotEqual(IntPtr.Zero, renderer.CurrentHIcon);

        renderer.UpdateIcon(UpsOperationalState.BatteryLow, 15, dpi: 144);
        Assert.NotEqual(IntPtr.Zero, renderer.CurrentHIcon);

        renderer.UpdateIcon(UpsOperationalState.ChargingAvr, 90, dpi: 96);
        Assert.NotEqual(IntPtr.Zero, renderer.CurrentHIcon);

        renderer.UpdateIcon(UpsOperationalState.Fault, 0, dpi: 96);
        Assert.NotEqual(IntPtr.Zero, renderer.CurrentHIcon);

        renderer.SetTooltip("VoltTray: En línea (100%)");
        Assert.Equal("VoltTray: En línea (100%)", renderer.CurrentTooltip);
    }

    [Fact]
    public void PopoverPainter_Placement_EnforcesExact372PxAndClampsToWorkArea()
    {
        var iconRect = new ViewRect(1800, 1040, 1824, 1064);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        var bounds = PopoverPainter.CalculatePlacement(iconRect, TaskbarEdge.Bottom, workArea, cardHeight: 320);

        Assert.Equal(DesignTokens.Sizes.PopoverWidth, bounds.Width);
        Assert.Equal(372, bounds.Width);
        Assert.Equal(320, bounds.Height);
        Assert.True(bounds.Right <= workArea.Right);
        Assert.True(bounds.Top < iconRect.Top);
    }

    [Fact]
    public void PopoverPainter_FiresSelfTestAndBeeperEvents()
    {
        var painter = new PopoverPainter();
        bool testFired = false;
        bool beeperFired = false;

        painter.OnSelfTestRequested += () => testFired = true;
        painter.OnToggleBeeperRequested += () => beeperFired = true;

        painter.TriggerSelfTest();
        painter.TriggerToggleBeeper();

        Assert.True(testFired);
        Assert.True(beeperFired);
    }

    [Fact]
    public void ExitDialog_Enforces44PxButtonHeightAndReturnsChoices()
    {
        Assert.True(ExitDialog.ActionButtonHeight >= 44);
        Assert.Equal(380, ExitDialog.DialogWidth);
        Assert.Equal(220, ExitDialog.DialogHeight);

        var dialog = new ExitDialog();
        dialog.MockDialogHandler = _ => ExitDialogResult.Pause;

        var result = dialog.Show(UITheme.Dark);
        Assert.Equal(ExitDialogResult.Pause, result);
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440, 0, 48, 2560, 1440, TaskbarEdge.Top)]
    [InlineData(0, 0, 2560, 1440, 64, 0, 2560, 1440, TaskbarEdge.Left)]
    [InlineData(0, 0, 2560, 1440, 0, 0, 2496, 1440, TaskbarEdge.Right)]
    [InlineData(0, 0, 2560, 1440, 0, 0, 2560, 1392, TaskbarEdge.Bottom)]
    public void Win32ScreenPlacementProvider_InferTaskbarEdge_DetectsAllFourMonitorEdges(
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        TaskbarEdge expectedEdge)
    {
        var monitorBounds = new ViewRect(monitorLeft, monitorTop, monitorRight, monitorBottom);
        var workArea = new ViewRect(workLeft, workTop, workRight, workBottom);

        TaskbarEdge edge = Win32ScreenPlacementProvider.InferTaskbarEdge(monitorBounds, workArea);
        Assert.Equal(expectedEdge, edge);
    }

    [Fact]
    public void PopoverViewModel_OmitsUnavailableMetrics_AndUsesDynamicModelTitle()
    {
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault() with
        {
            TemperatureCelsius = null,
            ActiveLoadWatts = null,
            FaultReferenceVoltageAc = null,
            BankSpec = new BatteryBankSpec(SeriesBatteryCount: 2, NominalVoltageDc: 24.0, RatedCapacityAh: 9.0, Label: "Forza NT-2401PRO")
        };

        PopoverViewModel vm = PopoverViewModel.FromTelemetry(telemetry, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Forza NT-2401PRO", vm.Title);
        Assert.Equal("FN", vm.Emblem);
        Assert.Null(vm.TemperatureText);
        Assert.Null(vm.ActiveLoadWattsText);
        Assert.Null(vm.FaultReferenceText);
    }

    [Fact]
    public void PopoverPainter_Debounce350Ms_PreventsImmediateReopenAfterHide_AndAllowsKeyboardActions()
    {
        DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        using var painter = new PopoverPainter(enableNativeWindow: false, clock: () => now);
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();
        var anchor = new ViewRect(1800, 1040, 1824, 1064);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        bool beeperFired = false;
        painter.OnToggleBeeperRequested += () => beeperFired = true;

        painter.Show(telemetry, UITheme.Dark, anchor, TaskbarEdge.Bottom, workArea);
        Assert.True(painter.IsVisible);

        painter.Hide();
        Assert.False(painter.IsVisible);

        now = now.AddMilliseconds(100);
        painter.Show(telemetry, UITheme.Dark, anchor, TaskbarEdge.Bottom, workArea);
        Assert.False(painter.IsVisible);

        now = now.AddMilliseconds(300);
        painter.Show(telemetry, UITheme.Dark, anchor, TaskbarEdge.Bottom, workArea);
        Assert.True(painter.IsVisible);

        Assert.True(painter.HandleKeyDown(LocalizationConstants.VkTab));
        Assert.True(painter.HandleKeyDown(LocalizationConstants.VkReturn));
        Assert.True(beeperFired);

        Assert.True(painter.HandleKeyDown(LocalizationConstants.VkEscape));
        Assert.False(painter.IsVisible);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void PopoverPainter_CalculatePlacement_HandlesAllFourTaskbarEdgesWithinWorkArea(TaskbarEdge edge)
    {
        var workArea = new ViewRect(0, 0, 1920, 1040);
        var iconRect = edge switch
        {
            TaskbarEdge.Top => new ViewRect(1800, 8, 1824, 32),
            TaskbarEdge.Left => new ViewRect(8, 900, 32, 924),
            TaskbarEdge.Right => new ViewRect(1880, 900, 1904, 924),
            _ => new ViewRect(1800, 1016, 1824, 1040)
        };

        ViewRect bounds = PopoverPainter.CalculatePlacement(iconRect, edge, workArea);

        Assert.Equal(DesignTokens.Sizes.PopoverWidth, bounds.Width);
        Assert.True(bounds.Left >= workArea.Left);
        Assert.True(bounds.Right <= workArea.Right);
        Assert.True(bounds.Top >= workArea.Top);
        Assert.True(bounds.Bottom <= workArea.Bottom);
    }

    [Fact]
    public void TrayIconRasterizer_Renders32BppArgbWithTransparentBackground_DistinctStateShapes_AndTaskbarThemeColors()
    {
        Assert.Equal(16, TrayIconRasterizer.ResolvePhysicalSize(96));
        Assert.Equal(20, TrayIconRasterizer.ResolvePhysicalSize(120));
        Assert.Equal(24, TrayIconRasterizer.ResolvePhysicalSize(144));
        Assert.Equal(32, TrayIconRasterizer.ResolvePhysicalSize(192));

        uint[] darkOnline = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.LineNormal, 100, 20, isLightTaskbar: false);
        uint[] lightOnline = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.LineNormal, 100, 20, isLightTaskbar: true);
        uint[] onBattery = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.OnBattery, 50, 20, isLightTaskbar: false);
        uint[] fault = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.Fault, 0, 20, isLightTaskbar: false);
        uint[] paused = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.Paused, 0, 20, isLightTaskbar: false);
        uint[] noComm = TrayIconRasterizer.RenderArgbPixels(UpsOperationalState.CommunicationsLost, 0, 20, isLightTaskbar: false);

        Assert.Equal(0x00u, darkOnline[0] >> 24);
        Assert.Contains(darkOnline, px => (px >> 24) == 0xFF);
        Assert.NotEqual(darkOnline, lightOnline);
        Assert.NotEqual(darkOnline, onBattery);
        Assert.NotEqual(onBattery, fault);
        Assert.NotEqual(fault, paused);
        Assert.NotEqual(paused, noComm);
    }

    [Fact]
    public void TrayRenderer_HandlesTaskbarCreatedMessage_WithoutLeakingIconHandle()
    {
        using var renderer = new TrayRenderer(() => true);
        renderer.UpdateIcon(UpsOperationalState.LineNormal, 100, dpi: 192);
        IntPtr iconBefore = renderer.CurrentHIcon;
        Assert.NotEqual(IntPtr.Zero, iconBefore);

        if (renderer.TaskbarCreatedMessageId != 0)
        {
            Assert.True(renderer.HandleWindowMessage(renderer.TaskbarCreatedMessageId, IntPtr.Zero));
        }

        Assert.Equal(iconBefore, renderer.CurrentHIcon);
    }
}
