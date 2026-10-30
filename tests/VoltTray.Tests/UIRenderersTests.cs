using VoltTray.Core.Models;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

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

        Assert.Equal(PopoverPainter.PopoverWidth, bounds.Width);
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
}
