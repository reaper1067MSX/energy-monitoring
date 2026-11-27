using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using System.Globalization;
using SkiaSharp;
using VoltTray.Core.Battery;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Atoms;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Models;
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
        Assert.Equal("Line-Int", vm.RegulationModeText);
        Assert.Equal("216 Wh", vm.BankEnergyWhText);
        Assert.NotNull(vm.CellsSummaryText);
        Assert.Null(vm.TemperatureText);
        Assert.Null(vm.ActiveLoadWattsText);
        Assert.Null(vm.FaultReferenceText);

        UpsTelemetry distinctFault = telemetry with
        {
            InputVoltageAc = 117.3,
            FaultReferenceVoltageAc = 98.5
        };
        PopoverViewModel faultVm = PopoverViewModel.FromTelemetry(distinctFault, CultureInfo.GetCultureInfo("es-ES"));
        Assert.Equal("98.5 V", faultVm.FaultReferenceText);
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
        ITrayIconRasterizer rasterizer = TrayIconRasterizer.CreateDefault();

        Assert.Equal(16, rasterizer.ResolvePhysicalSize(96));
        Assert.Equal(20, rasterizer.ResolvePhysicalSize(120));
        Assert.Equal(24, rasterizer.ResolvePhysicalSize(144));
        Assert.Equal(32, rasterizer.ResolvePhysicalSize(192));

        uint[] darkOnline = rasterizer.RenderArgbPixels(UpsOperationalState.LineNormal, 100, 20, isLightTaskbar: false);
        uint[] lightOnline = rasterizer.RenderArgbPixels(UpsOperationalState.LineNormal, 100, 20, isLightTaskbar: true);
        uint[] onBattery = rasterizer.RenderArgbPixels(UpsOperationalState.OnBattery, 50, 20, isLightTaskbar: false);
        uint[] fault = rasterizer.RenderArgbPixels(UpsOperationalState.Fault, 0, 20, isLightTaskbar: false);
        uint[] paused = rasterizer.RenderArgbPixels(UpsOperationalState.Paused, 0, 20, isLightTaskbar: false);
        uint[] noComm = rasterizer.RenderArgbPixels(UpsOperationalState.CommunicationsLost, 0, 20, isLightTaskbar: false);

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
        using var renderer = new TrayRenderer(isLightTaskbarProvider: () => true);
        renderer.UpdateIcon(UpsOperationalState.LineNormal, 100, dpi: 192);
        IntPtr iconBefore = renderer.CurrentHIcon;
        Assert.NotEqual(IntPtr.Zero, iconBefore);

        if (renderer.TaskbarCreatedMessageId != 0)
        {
            Assert.True(renderer.HandleWindowMessage(renderer.TaskbarCreatedMessageId, IntPtr.Zero));
        }

        Assert.Equal(iconBefore, renderer.CurrentHIcon);
    }

    [Theory]
    [InlineData(SvgIconConstants.TrayOnline)]
    [InlineData(SvgIconConstants.StatusCheck)]
    [InlineData(SvgIconConstants.TelemetryGrid)]
    public void SkiaSvgIconLibrary_LoadsAndRasterizesEmbeddedHandoffSvgs_AsPremultipliedArgb(string iconName)
    {
        using ISvgIconLibrary library = new SkiaSvgIconLibrary();
        uint[]? pixels = library.RenderArgbPixels(iconName, 24, SKColors.White);

        Assert.NotNull(pixels);
        Assert.Equal(24 * 24, pixels.Length);
        Assert.Contains(pixels, px => (px >> 24) > 0);
        Assert.All(pixels, px =>
        {
            byte a = (byte)(px >> 24);
            byte r = (byte)((px >> 16) & 0xFF);
            byte g = (byte)((px >> 8) & 0xFF);
            byte b = (byte)(px & 0xFF);
            Assert.True(r <= a && g <= a && b <= a);
        });
    }

    [Fact]
    public void SkiaPopoverSurfaceRenderer_RendersDistinctLightAndDarkSplitCockpitCards_AndHandlesUnavailableMetrics()
    {
        using ISvgIconLibrary library = new SkiaSvgIconLibrary();
        using IPopoverSurfaceRenderer renderer = new SkiaPopoverSurfaceRenderer(library);

        UpsTelemetry telemetry = UpsTelemetry.CreateDefault() with
        {
            TemperatureCelsius = null,
            ActiveLoadWatts = null,
            FaultReferenceVoltageAc = null,
            EstimatedRuntimeMinutes = 25
        };
        PopoverViewModel vm = PopoverViewModel.FromTelemetry(telemetry, CultureInfo.GetCultureInfo("es-ES"));
        int width = DesignTokens.Sizes.PopoverWidth;
        int height = DesignTokens.Sizes.PopoverCardHeight;

        byte[] darkBytes = renderer.RenderBgra32(new PopoverRenderState(vm, UITheme.Dark, width, height, PopoverActionId.SelfTest));
        byte[] lightBytes = renderer.RenderBgra32(new PopoverRenderState(vm, UITheme.Light, width, height, PopoverActionId.ToggleBeeper));

        Assert.Equal(width * height * 4, darkBytes.Length);
        Assert.Equal(width * height * 4, lightBytes.Length);
        Assert.NotEqual(darkBytes, lightBytes);

        byte[] darkPng = renderer.RenderPng(new PopoverRenderState(vm, UITheme.Dark, width, height));
        Assert.True(darkPng.Length > 1000);
    }

    [Fact]
    public void PopoverPainter_PointerHitTesting_TriggersButtonEventsWithin44PxTargetsOnly()
    {
        using var painter = new PopoverPainter(enableNativeWindow: false);
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();
        var anchor = new ViewRect(1800, 1040, 1824, 1064);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        int selfTestCount = 0;
        int beeperCount = 0;
        painter.OnSelfTestRequested += () => selfTestCount++;
        painter.OnToggleBeeperRequested += () => beeperCount++;

        painter.Show(telemetry, UITheme.Light, anchor, TaskbarEdge.Bottom, workArea);
        var layout = new PopoverHitTestLayout(DesignTokens.Sizes.PopoverWidth, DesignTokens.Sizes.PopoverCardHeight);

        Assert.True(painter.HandlePointerDown(layout.SelfTestButton.Left + 10, layout.SelfTestButton.Top + 10));
        Assert.True(painter.HandlePointerUp(layout.SelfTestButton.Left + 10, layout.SelfTestButton.Top + 10));

        Assert.True(painter.HandlePointerDown(layout.ToggleBeeperButton.Left + 10, layout.ToggleBeeperButton.Top + 10));
        Assert.True(painter.HandlePointerUp(layout.ToggleBeeperButton.Left + 10, layout.ToggleBeeperButton.Top + 10));

        Assert.False(painter.HandlePointerDown(20, 20));
        Assert.False(painter.HandlePointerUp(20, 20));

        Assert.Equal(1, selfTestCount);
        Assert.Equal(1, beeperCount);
    }

    [Fact]
    public void Win32ContextMenu_ReturnsMockedAction_AndReturnsNoneWhenNativeMenuDisabled()
    {
        var menu = new Win32ContextMenu(enableNativeMenu: false);
        var state = new ContextMenuState(IsPaused: false, IsBeeperMuted: true, IsTestInProgress: false);

        Assert.Equal(ContextMenuAction.None, menu.Show(state));

        menu.MockMenuHandler = s => s.IsBeeperMuted ? ContextMenuAction.ToggleBeeper : ContextMenuAction.Open;
        Assert.Equal(ContextMenuAction.ToggleBeeper, menu.Show(state));
    }

    [Fact]
    public void FluentBatterySvgBuilder_GeneratesStyleAViewBox_ProportionalBar_AndDistinctStateBadges()
    {
        string fullOnline = FluentBatterySvgBuilder.BuildIconSvg(UpsOperationalState.LineNormal, 100, isLightTaskbar: false);
        string halfBattery = FluentBatterySvgBuilder.BuildIconSvg(UpsOperationalState.OnBattery, 50, isLightTaskbar: true);
        string zeroBattery = FluentBatterySvgBuilder.BuildIconSvg(UpsOperationalState.BatteryLow, 0, isLightTaskbar: true);
        string noComm = FluentBatterySvgBuilder.BuildIconSvg(UpsOperationalState.CommunicationsLost, 100, isLightTaskbar: false);

        Assert.Contains("viewBox=\"0 0 28 16\"", fullOnline, StringComparison.Ordinal);
        Assert.Contains("width=\"17.6\"", fullOnline, StringComparison.Ordinal);
        Assert.Contains("#4ade80", fullOnline, StringComparison.Ordinal);
        Assert.Contains("M13.4 1.2L8.9 8.2H12.4L11.4 14L16.1 6.8H12.6L13.4 1.2Z", fullOnline, StringComparison.Ordinal);

        Assert.Contains("width=\"8.8\"", halfBattery, StringComparison.Ordinal);
        Assert.Contains("#d97706", halfBattery, StringComparison.Ordinal);

        Assert.DoesNotContain("rx=\"1.2\"", zeroBattery, StringComparison.Ordinal);
        Assert.DoesNotContain("rx=\"1.2\"", noComm, StringComparison.Ordinal);
        Assert.Contains("M10.2 5.7L14.8 10.3", noComm, StringComparison.Ordinal);
    }
}
