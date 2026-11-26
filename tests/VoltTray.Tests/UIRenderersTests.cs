using VoltTray.Core.Models;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Models;
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
}
