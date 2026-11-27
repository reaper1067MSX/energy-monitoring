using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltTray.Core.Telemetry;
using VoltTray.UI.Models;
using VoltTray.UI.Popover;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;

namespace VoltTray.Tests;

[Trait("Category", "Win32Integration")]
public partial class Win32UiIntegrationTests
{
    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    [Fact]
    public void NativeWin32Surfaces_UpdateRecoverAndDispose_WithoutPerFrameGdiHandleLeaks()
    {
        IntPtr processHandle = Process.GetCurrentProcess().Handle;
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();
        var anchor = new ViewRect(1700, 1032, 1732, 1064);
        var workArea = new ViewRect(0, 0, 1920, 1040);

        DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        using var tray = new TrayRenderer(isLightTaskbarProvider: () => false);
        using var popover = new PopoverPainter(enableNativeWindow: true, clock: () => now);
        using var pill = new TaskbarPillWindow(
            new TaskbarPillPlacementCalculator(),
            new TaskbarPillSurfaceRenderer(TrayIconRasterizer.CreateDefault()),
            new Win32FullscreenStateProvider(),
            isLightTaskbarProvider: () => false,
            enableNativeWindow: true);

        // Warm up one-time Win32/DWM/Shell initialization before measuring per-frame delta.
        RunFrameTick(tray, popover, pill, telemetry, anchor, workArea);
        now = now.AddMilliseconds(400);

        uint gdiBeforePolling = GetGuiResources(processHandle, 0);
        for (int i = 0; i < 20; i++)
        {
            tray.UpdateIcon(telemetry.State, telemetry.BatteryPercentage, dpi: 96);
            tray.SetTooltip("VoltTray Smoke");
            pill.Update(telemetry);
            pill.HandleWindowMessage(0x0400 + 102, IntPtr.Zero, IntPtr.Zero);
            pill.HandleWindowMessage(0x02E0, (IntPtr)144, IntPtr.Zero);
        }
        uint gdiAfterPolling = GetGuiResources(processHandle, 0);

        popover.Show(telemetry, UITheme.Dark, anchor, TaskbarEdge.Bottom, workArea);
        Assert.True(popover.IsVisible);
        popover.Hide();
        Assert.False(popover.IsVisible);

        Assert.True(
            gdiAfterPolling <= gdiBeforePolling + 2,
            $"Per-frame polling GDI leak detected across 20 ticks: before={gdiBeforePolling}, after={gdiAfterPolling}");
    }

    private static void RunFrameTick(
        TrayRenderer tray,
        PopoverPainter popover,
        TaskbarPillWindow pill,
        UpsTelemetry telemetry,
        ViewRect anchor,
        ViewRect workArea)
    {
        tray.UpdateIcon(telemetry.State, telemetry.BatteryPercentage, dpi: 96);
        tray.SetTooltip("VoltTray Smoke");

        popover.Show(telemetry, UITheme.Dark, anchor, TaskbarEdge.Bottom, workArea);
        Assert.True(popover.IsVisible);
        popover.Hide();

        pill.Update(telemetry);
        pill.HandleWindowMessage(0x0400 + 102, IntPtr.Zero, IntPtr.Zero);
        pill.HandleWindowMessage(0x02E0, (IntPtr)144, IntPtr.Zero);
    }
}
