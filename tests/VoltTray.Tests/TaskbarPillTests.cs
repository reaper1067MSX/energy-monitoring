using VoltTray.Core.Telemetry;
using VoltTray.Hosting;
using VoltTray.UI.Models;
using VoltTray.UI.TaskbarPill;
using VoltTray.UI.Tray;

namespace VoltTray.Tests;

public class TaskbarPillTests
{
    [Fact]
    public void ApplicationOptionsParser_TaskbarPillFlag_IsOptInAndRejectedWithInspect()
    {
        TrayMonitorLaunchOptions defaultOptions = Assert.IsType<TrayMonitorLaunchOptions>(ApplicationOptionsParser.Parse([]));
        Assert.False(defaultOptions.EnableTaskbarPill);

        TrayMonitorLaunchOptions enabledOptions = Assert.IsType<TrayMonitorLaunchOptions>(
            ApplicationOptionsParser.Parse(["--taskbar-pill"]));
        Assert.True(enabledOptions.EnableTaskbarPill);

        Assert.IsType<InvalidLaunchOptions>(ApplicationOptionsParser.Parse(["--inspect", "--taskbar-pill"]));
    }

    [Fact]
    public void TaskbarPillPlacementCalculator_Win11ColdBootEmptyTrayNotify_UsesFallbackCentersToStartAndAvoidsOverlap()
    {
        var calculator = new TaskbarPillPlacementCalculator();
        var snapshot = new TaskbarPillLayoutSnapshot(
            TaskbarRect: new ViewRect(0, 1032, 1920, 1080),
            TrayNotifyRect: new ViewRect(0, 0, 0, 0),
            StartButtonRect: new ViewRect(760, 1036, 800, 1076),
            WidgetRect: new ViewRect(8, 1036, 160, 1076),
            AppListRect: new ViewRect(800, 1036, 1120, 1076));

        ViewRect bounds = calculator.Calculate(snapshot);

        Assert.Equal(144, bounds.Width);
        Assert.Equal(48, bounds.Height);
        Assert.Equal(1920 - 220 - 144, bounds.Left);
        Assert.Equal(1032, bounds.Top);
        Assert.True(bounds.Left >= snapshot.AppListRect!.Value.Right + 6);
    }

    [Fact]
    public void TaskbarPillSurfaceRenderer_FillsInteriorWithMinAlpha1ForHitTesting_AndAdaptsToTaskbarTheme()
    {
        var renderer = new TaskbarPillSurfaceRenderer(TrayIconRasterizer.CreateDefault());
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();

        uint[] darkPixels = renderer.RenderArgbPixels(telemetry, 144, 48, isLightTaskbar: false, dpi: 96);
        uint[] lightPixels = renderer.RenderArgbPixels(telemetry, 144, 48, isLightTaskbar: true, dpi: 96);

        Assert.Equal(144 * 48, darkPixels.Length);
        Assert.All(darkPixels, px => Assert.True((px >> 24) >= 1));
        Assert.NotEqual(darkPixels, lightPixels);
    }

    [Fact]
    public void TaskbarPillWindow_HandlesClicksFullscreenAutoHideDpiChangeAndTaskbarCreated()
    {
        var fullscreen = new ToggleableFullscreenProvider();
        using var pill = new TaskbarPillWindow(
            new TaskbarPillPlacementCalculator(),
            new TaskbarPillSurfaceRenderer(TrayIconRasterizer.CreateDefault()),
            fullscreen,
            isLightTaskbarProvider: () => false,
            enableNativeWindow: false);

        int leftClicks = 0;
        int rightClicks = 0;
        pill.OnPillLeftClicked += () => leftClicks++;
        pill.OnPillRightClicked += () => rightClicks++;

        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();
        pill.Update(telemetry);
        Assert.True(pill.IsVisible);

        const uint wmLButtonUp = 0x0202;
        const uint wmRButtonUp = 0x0205;
        const uint wmDpiChanged = 0x02E0;

        Assert.True(pill.HandleWindowMessage(wmLButtonUp, IntPtr.Zero, IntPtr.Zero));
        Assert.True(pill.HandleWindowMessage(wmRButtonUp, IntPtr.Zero, IntPtr.Zero));
        Assert.Equal(1, leftClicks);
        Assert.Equal(1, rightClicks);

        Assert.True(pill.HandleWindowMessage(wmDpiChanged, (IntPtr)144, IntPtr.Zero));
        Assert.Equal(144, pill.CurrentDpi);

        fullscreen.IsFullscreen = true;
        pill.Update(telemetry);
        Assert.False(pill.IsVisible);
    }

    private sealed class ToggleableFullscreenProvider : IFullscreenStateProvider
    {
        public bool IsFullscreen { get; set; }
        public bool IsFullscreenActive() => IsFullscreen;
    }
}
