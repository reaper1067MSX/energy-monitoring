using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Views;

namespace VoltTray.Tests;

public class HostLifecycleTests
{
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private sealed class MockUpsPort : IUpsDevicePort
    {
        public bool IsConnected { get; set; } = true;
        public string? ActiveDevicePath => IsConnected ? "MockPath" : null;
        public int DisconnectCalls { get; private set; }
        public int TryConnectCalls { get; private set; }
        public string? Response { get; set; }
        public bool TryConnect(string? p = null) { TryConnectCalls++; IsConnected = true; return true; }
        public void Disconnect() { DisconnectCalls++; IsConnected = false; }
        public Task<string?> QueryAsync(string c, CancellationToken ct = default) => Task.FromResult(Response);
        public Task<bool> SendCommandAsync(string c, CancellationToken ct = default) => Task.FromResult(true);
        public void Dispose() => Disconnect();
    }
    private sealed class MockTrayIcon : ITrayIconView
    {
        public UpsOperationalState? LastState { get; set; }
        public int LastBatteryPct { get; set; }
        public string? LastTooltip { get; set; }
        public void UpdateIcon(UpsOperationalState s, int b, int dpi) { LastState = s; LastBatteryPct = b; }
        public void SetTooltip(string text) => LastTooltip = text;
        public void Recreate() { }
        public void Dispose() { }
    }
    private sealed class MockPopover : IPopoverView
    {
        public bool IsVisible { get; set; }
        public UpsTelemetry? LastTelemetry { get; set; }
        public void Show(UpsTelemetry t, UITheme th, ViewRect r, TaskbarEdge e, ViewRect w) => LastTelemetry = t;
        public void Hide() => IsVisible = false;
        public void Dispose() => Hide();
    }
    private sealed class FakeShutdownPort : ISafetyShutdownPort { public int Calls { get; private set; } public bool InitiateShutdown(bool d, string r) { Calls++; return true; } public bool AbortShutdown() => true; }
    private sealed class FakeNotificationPort : INotificationPort { public void ShowNotification(string t, string m, NotificationSeverity s) { } }
    private sealed class MockExitDialog : IExitDialogView { public ExitDialogResult Result { get; set; } = ExitDialogResult.Cancel; public ExitDialogResult Show(UITheme t, nint o = 0) => Result; }

    private static readonly string GoldenFrameString = Encoding.Latin1.GetString([0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20, 0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20, 0x08, 0x20, 0xA1, 0x0D]);
    private static readonly string OutageLowString = Encoding.Latin1.GetString([0x23, 0x73, 0x01, 0x20, 0x34, 0x20, 0x73, 0x01, 0x20, 0x34, 0x20, 0x0E, 0x20, 0x50, 0x0C, 0x20, 0x12, 0xC0, 0x20, 0xE9, 0x20, 0x3C, 0x20, 0xC0, 0x20, 0xA1, 0x0D]);

    [Fact]
    public void SingleInstance_AllowsFirst_AndRejectsSecondary()
    {
        string name = $@"Global\VoltTray_Test_{Guid.NewGuid():N}";
        using var first = SingleInstanceManager.TryAcquire(name);
        using var second = SingleInstanceManager.TryAcquire(name);
        Assert.NotNull(first); Assert.Null(second);
        first.ReleaseMutex(); first.Dispose();
        using var third = SingleInstanceManager.TryAcquire(name);
        Assert.NotNull(third); third.ReleaseMutex();
    }

    [Fact]
    public async Task Lifecycle_PollCycle_DecodesTelemetry_AndDispatchesToTray()
    {
        using var port = new MockUpsPort { Response = GoldenFrameString };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover { IsVisible = false };
        using var lifecycle = new HostLifecycle(port, tray, popover);
        await lifecycle.StepAsync();
        Assert.Equal(UpsOperationalState.LineNormal, tray.LastState);
        Assert.Equal(100, tray.LastBatteryPct);
        Assert.Contains("Online", tray.LastTooltip);
        Assert.Null(popover.LastTelemetry);
    }

    [Fact]
    public async Task Lifecycle_PollCycle_WhenPopoverVisible_DispatchesTelemetry()
    {
        using var port = new MockUpsPort { Response = GoldenFrameString };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover { IsVisible = true };
        using var lifecycle = new HostLifecycle(port, tray, popover);
        await lifecycle.StepAsync();
        Assert.NotNull(popover.LastTelemetry);
        Assert.Equal(14, popover.LastTelemetry.LoadPercentage);
        Assert.Equal(126, popover.LastTelemetry.ActiveLoadWatts);
    }

    [Fact]
    public async Task Lifecycle_ShutdownCoordinator_EvaluatesSafetyDebounce()
    {
        using var port = new MockUpsPort { Response = OutageLowString };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        var shutdown = new FakeShutdownPort();
        using var lifecycle = new HostLifecycle(port, shutdown, new FakeNotificationPort(), tray, popover);
        lifecycle.ShutdownCoordinator.DryRun = false;
        await lifecycle.StepAsync(); Assert.Equal(0, shutdown.Calls);
        await lifecycle.StepAsync(); Assert.Equal(1, shutdown.Calls);
    }

    [Fact]
    public async Task Lifecycle_CommunicationFailure_After3Attempts_SetsCommLostState()
    {
        using var port = new MockUpsPort { Response = null };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        using var lifecycle = new HostLifecycle(port, tray, popover);
        for (int i = 0; i < 3; i++) await lifecycle.StepAsync();
        Assert.Equal(UpsOperationalState.CommunicationsLost, tray.LastState);
        Assert.Contains("No Communication", tray.LastTooltip);
    }

    [Fact]
    public void Lifecycle_Pause_ReleasesUsbPortImmediately_AndSetsPausedState()
    {
        using var port = new MockUpsPort();
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        using var lifecycle = new HostLifecycle(port, tray, popover);
        lifecycle.Pause(TimeSpan.FromMinutes(15));
        Assert.True(lifecycle.IsPaused); Assert.True(port.DisconnectCalls >= 1);
        Assert.Equal(UpsOperationalState.Paused, tray.LastState); Assert.Contains("Paused", tray.LastTooltip);
        lifecycle.Resume();
        Assert.False(lifecycle.IsPaused); Assert.True(port.TryConnectCalls >= 1);
    }

    [Fact]
    public async Task Lifecycle_AutoResumeTimer_ResumesAfterElapsedDuration()
    {
        using var port = new MockUpsPort();
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        using var lifecycle = new HostLifecycle(port, tray, popover);
        lifecycle.Pause(TimeSpan.FromMilliseconds(50));
        Assert.True(lifecycle.IsPaused);
        await Task.Delay(120);
        Assert.False(lifecycle.IsPaused); Assert.True(port.TryConnectCalls >= 1);
    }

    [Fact]
    public void Lifecycle_ExitDialog_PauseAndCloseActions()
    {
        using var port = new MockUpsPort();
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        var dialog = new MockExitDialog();
        using var lifecycle = new HostLifecycle(port, null, null, tray, popover, dialog);
        bool exitRequested = false;
        lifecycle.ExitAction = () => exitRequested = true;
        dialog.Result = ExitDialogResult.Pause;
        Assert.Equal(ExitDialogResult.Pause, lifecycle.ShowExitDialog()); Assert.True(lifecycle.IsPaused);
        dialog.Result = ExitDialogResult.Close;
        Assert.Equal(ExitDialogResult.Close, lifecycle.ShowExitDialog()); Assert.True(exitRequested);
    }

    [Fact]
    public async Task MemorySLA_StandbyPrivateWorkingSet_RemainsUnder25MB()
    {
        using var port = new MockUpsPort { Response = GoldenFrameString };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover { IsVisible = false };
        using var lifecycle = new HostLifecycle(port, tray, popover);
        for (int i = 0; i < 5; i++) await lifecycle.StepAsync();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var proc = Process.GetCurrentProcess(); EmptyWorkingSet(proc.Handle); proc.Refresh();
        Assert.True(proc.WorkingSet64 < 25L * 1024 * 1024, $"WS: {proc.WorkingSet64 / (1024 * 1024)} MB");
        Assert.True(proc.PrivateMemorySize64 < 25L * 1024 * 1024, $"Priv: {proc.PrivateMemorySize64 / (1024 * 1024)} MB");
    }

    [Fact]
    public async Task MemorySLA_ActivePopover_RemainsUnder80MB()
    {
        using var port = new MockUpsPort { Response = GoldenFrameString };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover { IsVisible = true };
        using var lifecycle = new HostLifecycle(port, tray, popover);
        for (int i = 0; i < 15; i++) await lifecycle.StepAsync();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var proc = Process.GetCurrentProcess(); proc.Refresh();
        Assert.True(proc.WorkingSet64 < 80L * 1024 * 1024, $"WS: {proc.WorkingSet64 / (1024 * 1024)} MB");
    }
}
