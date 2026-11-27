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
    public void TaskbarPillPlacementCalculator_Win11ColdBootEmptyTrayNotify_UsesFallbackCentersToStartAndClampsWidth()
    {
        var calculator = new TaskbarPillPlacementCalculator();
        var coldBootSnapshot = new TaskbarPillLayoutSnapshot(
            TaskbarRect: new ViewRect(0, 1032, 1920, 1080),
            TrayNotifyRect: new ViewRect(0, 0, 0, 0),
            StartButtonRect: new ViewRect(760, 1036, 800, 1076),
            WidgetRect: new ViewRect(8, 1036, 160, 1076),
            AppListRect: new ViewRect(800, 1036, 1120, 1076),
            PillWidth: 80,
            PillHeight: 34);

        ViewRect bounds = calculator.Calculate(coldBootSnapshot);
        Assert.Equal(80, bounds.Width);
        Assert.Equal(34, bounds.Height);
        Assert.Equal(1920 - 220 - 80, bounds.Left);
        Assert.Equal(1039, bounds.Top);
        Assert.True(bounds.Left >= coldBootSnapshot.AppListRect!.Value.Right + 6);

        var tightSnapshot = new TaskbarPillLayoutSnapshot(
            TaskbarRect: new ViewRect(0, 1032, 1920, 1080),
            TrayNotifyRect: new ViewRect(1700, 1032, 1920, 1080),
            StartButtonRect: new ViewRect(760, 1036, 800, 1076),
            WidgetRect: null,
            AppListRect: new ViewRect(800, 1036, 1600, 1076));

        ViewRect clamped = calculator.Calculate(tightSnapshot);
        Assert.True(clamped.Left >= 1606);
        Assert.True(clamped.Right <= 1694);
    }

    [Fact]
    public void TaskbarPillSurfaceRenderer_FillsPremultipliedArgbWithMinAlpha1_AndAdaptsToTaskbarTheme()
    {
        var renderer = new TaskbarPillSurfaceRenderer(TrayIconRasterizer.CreateDefault());
        UpsTelemetry telemetry = UpsTelemetry.CreateDefault();

        uint[] darkPixels = renderer.RenderArgbPixels(telemetry, 80, 34, isLightTaskbar: false, dpi: 96);
        uint[] lightPixels = renderer.RenderArgbPixels(telemetry, 80, 34, isLightTaskbar: true, dpi: 96);
        uint[] hoveredPixels = renderer.RenderArgbPixels(telemetry, 80, 34, isLightTaskbar: false, dpi: 96, isHovered: true);
        Assert.NotEqual(darkPixels, hoveredPixels);

        string tooltip = TaskbarPillWindow.FormatTooltipText(telemetry);
        Assert.Contains("In 116.2V", tooltip, StringComparison.Ordinal);
        Assert.Contains("Out 116.2V", tooltip, StringComparison.Ordinal);
        Assert.Contains("Bat 100%", tooltip, StringComparison.Ordinal);

        Assert.Equal(80 * 34, darkPixels.Length);
        Assert.All(darkPixels, px =>
        {
            byte a = (byte)(px >> 24);
            byte r = (byte)((px >> 16) & 0xFF);
            byte g = (byte)((px >> 8) & 0xFF);
            byte b = (byte)(px & 0xFF);
            Assert.True(a >= 1);
            Assert.True(r <= a && g <= a && b <= a);
        });
        Assert.All(lightPixels, px =>
        {
            byte a = (byte)(px >> 24);
            byte r = (byte)((px >> 16) & 0xFF);
            byte g = (byte)((px >> 8) & 0xFF);
            byte b = (byte)(px & 0xFF);
            Assert.True(a >= 1);
            Assert.True(r <= a && g <= a && b <= a);
        });
        Assert.NotEqual(darkPixels, lightPixels);

        UpsTelemetry onBattery = telemetry with
        {
            State = UpsOperationalState.OnBattery,
            BatteryPercentage = 45,
            LoadPercentage = 80
        };
        uint[] onBatteryPixels = renderer.RenderArgbPixels(onBattery, 144, 48, isLightTaskbar: false, dpi: 96);
        Assert.NotEqual(darkPixels, onBatteryPixels);

        byte[] png = renderer.RenderPng(onBattery, 144, 48, isLightTaskbar: false, dpi: 96);
        Assert.True(png.Length > 200);
    }

    [Fact]
    public void TaskbarPillWindow_HandlesClicksFullscreenAutoHideDpiScalingAndTaskbarCreated()
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
        Assert.Equal(80, pill.CurrentBounds.Width);
        Assert.Equal(34, pill.CurrentBounds.Height);

        const uint wmLButtonUp = 0x0202;
        const uint wmRButtonUp = 0x0205;
        const uint wmDpiChanged = 0x02E0;

        Assert.True(pill.HandleWindowMessage(wmLButtonUp, IntPtr.Zero, IntPtr.Zero));
        Assert.True(pill.HandleWindowMessage(wmRButtonUp, IntPtr.Zero, IntPtr.Zero));
        Assert.Equal(1, leftClicks);
        Assert.Equal(1, rightClicks);

        Assert.True(pill.HandleWindowMessage(wmDpiChanged, (IntPtr)144, IntPtr.Zero));
        Assert.Equal(144, pill.CurrentDpi);
        Assert.Equal(120, pill.CurrentBounds.Width);
        Assert.Equal(51, pill.CurrentBounds.Height);

        pill.HandleTaskbarCreated();
        Assert.Equal(1, pill.TaskbarRecoveryCount);
        Assert.True(pill.IsVisible);

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
