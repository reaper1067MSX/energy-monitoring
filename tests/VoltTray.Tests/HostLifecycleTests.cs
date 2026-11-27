using VoltTray.UI.Tray;
using VoltTray.UI.Popover;
using VoltTray.UI.Dialogs;
using VoltTray.Core.Telemetry;
using VoltTray.Core.Safety;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Shared;
using VoltTray.Tests.ObjectMothers;
using VoltTray.UI.Models;

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
        public int ToggleBeeperCalls { get; private set; }
        public int TriggerSelfTestCalls { get; private set; }
        public UpsTelemetry? NextTelemetry { get; set; }
        public bool ToggleBeeperResult { get; set; } = true;
        public bool TriggerSelfTestResult { get; set; } = true;
        public bool TryConnect(string? p = null) { TryConnectCalls++; IsConnected = true; return true; }
        public Task<Result<string>> ConnectAsync(string? p = null, CancellationToken ct = default)
        {
            TryConnect(p);
            return Task.FromResult(Result<string>.Success("MockPath"));
        }
        public void Disconnect() { DisconnectCalls++; IsConnected = false; }
        public Task<UpsTelemetry?> ReadTelemetryAsync(DateTimeOffset? now = null, CancellationToken ct = default) => Task.FromResult(NextTelemetry);
        public Task<bool> ToggleBeeperAsync(CancellationToken ct = default) { ToggleBeeperCalls++; return Task.FromResult(ToggleBeeperResult); }
        public Task<bool> TriggerSelfTestAsync(CancellationToken ct = default) { TriggerSelfTestCalls++; return Task.FromResult(TriggerSelfTestResult); }
        public void Dispose() => Disconnect();
    }
    private sealed class MockTrayIcon : ITrayIconView
    {
        public event Action? OnTrayIconClicked;
        public event Action? OnContextMenuRequested;

        public UpsOperationalState? LastState { get; set; }
        public int LastBatteryPct { get; set; }
        public string? LastTooltip { get; set; }
        public ViewRect? IconRect { get; set; }

        public void UpdateIcon(UpsOperationalState s, int b, int dpi)
        {
            LastState = s;
            LastBatteryPct = b;
        }

        public void SetTooltip(string text) => LastTooltip = text;
        public void Recreate() { }
        public ViewRect? TryGetIconRect() => IconRect;
        public void RaiseTrayIconClicked() => OnTrayIconClicked?.Invoke();
        public void RaiseContextMenuRequested() => OnContextMenuRequested?.Invoke();
        public void Dispose() { }
    }

    private sealed class MockPopover : IPopoverView
    {
        public event Action? OnSelfTestRequested;
        public event Action? OnToggleBeeperRequested;

        public bool IsVisible { get; set; }
        public UITheme CurrentTheme { get; set; } = UITheme.Dark;
        public UpsTelemetry? LastTelemetry { get; set; }
        public ViewRect? LastIconRect { get; private set; }
        public TaskbarEdge? LastEdge { get; private set; }
        public ViewRect? LastWorkArea { get; private set; }

        public void Show(UpsTelemetry t, UITheme th, ViewRect r, TaskbarEdge e, ViewRect w)
        {
            IsVisible = true;
            LastTelemetry = t;
            CurrentTheme = th;
            LastIconRect = r;
            LastEdge = e;
            LastWorkArea = w;
        }

        public void Hide() => IsVisible = false;
        public void RaiseSelfTestRequested() => OnSelfTestRequested?.Invoke();
        public void RaiseToggleBeeperRequested() => OnToggleBeeperRequested?.Invoke();
        public void Dispose() => Hide();
    }

    private sealed class FakeScreenPlacementProvider(ViewRect workArea, TaskbarEdge edge) : IScreenPlacementProvider
    {
        public (ViewRect WorkArea, TaskbarEdge Edge) GetWorkAreaForAnchor(ViewRect anchorRect)
        {
            return (workArea, edge);
        }
    }
    private sealed class FakeShutdownPort : ISafetyShutdownPort { public int Calls { get; private set; } public bool InitiateShutdown(bool d, string r) { Calls++; return true; } public bool AbortShutdown() => true; }
    private sealed class FakeNotificationPort : INotificationPort { public void ShowNotification(string t, string m, NotificationSeverity s) { } }
    private sealed class MockExitDialog : IExitDialogView { public ExitDialogResult Result { get; set; } = ExitDialogResult.Cancel; public ExitDialogResult Show(UITheme t, nint o = 0) => Result; }

    private static UpsTelemetry LineNormalTelemetry => TelemetryMother.CreateLineNormal(loadPct: 14, activeWatts: 126, runtimeMinutes: 25);
    private static UpsTelemetry OutageLowTelemetry => TelemetryMother.CreateBatteryLow();

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
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
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
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
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
        using var port = new MockUpsPort { NextTelemetry = OutageLowTelemetry };
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
        using var port = new MockUpsPort { NextTelemetry = null };
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
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
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
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover { IsVisible = true };
        using var lifecycle = new HostLifecycle(port, tray, popover);
        for (int i = 0; i < 15; i++) await lifecycle.StepAsync();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var proc = Process.GetCurrentProcess(); proc.Refresh();
        Assert.True(proc.WorkingSet64 < 80L * 1024 * 1024, $"WS: {proc.WorkingSet64 / (1024 * 1024)} MB");
    }

    [Fact]
    public async Task EventedViewInterfaces_HandleTrayPopoverAndExitDialogActionsWithoutDowncasts()
    {
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        var dialog = new MockExitDialog { Result = ExitDialogResult.Pause };
        var placement = new FakeScreenPlacementProvider(new ViewRect(0, 48, 2560, 1440), TaskbarEdge.Top);

        using var lifecycle = new HostLifecycle(
            port,
            null,
            null,
            tray,
            popover,
            dialog,
            placement);

        await lifecycle.StepAsync();
        tray.RaiseTrayIconClicked();
        Assert.True(popover.IsVisible);

        tray.RaiseTrayIconClicked();
        Assert.False(popover.IsVisible);

        popover.RaiseToggleBeeperRequested();
        popover.RaiseSelfTestRequested();
        Assert.Equal(1, port.ToggleBeeperCalls);
        Assert.Equal(1, port.TriggerSelfTestCalls);

        tray.RaiseContextMenuRequested();
        Assert.True(lifecycle.IsPaused);
    }

    [Fact]
    public async Task Placement_UsesFakeScreenPlacementProviderWorkAreaAndTopTaskbarEdge()
    {
        var monitorWorkArea = new ViewRect(0, 48, 2560, 1440);
        var liveTrayIconRect = new ViewRect(2410, 8, 2434, 32);
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
        using var tray = new MockTrayIcon { IconRect = liveTrayIconRect };
        using var popover = new MockPopover();
        var placement = new FakeScreenPlacementProvider(monitorWorkArea, TaskbarEdge.Top);

        using var lifecycle = new HostLifecycle(
            port,
            null,
            null,
            tray,
            popover,
            null,
            placement);

        await lifecycle.StepAsync();
        tray.RaiseTrayIconClicked();

        Assert.True(popover.IsVisible);
        Assert.Equal(liveTrayIconRect, popover.LastIconRect);
        Assert.Equal(monitorWorkArea, popover.LastWorkArea);
        Assert.Equal(TaskbarEdge.Top, popover.LastEdge);
    }

    [Fact]
    public async Task PollOnceAsync_QuarantinesImplausibleFrequency_PreservingLastKnownGoodAndSuppressingShutdown()
    {
        string tempDiagnosticsDir = Path.Combine(Path.GetTempPath(), $"volttray-host-diag-{Guid.NewGuid():N}");
        try
        {
            var probe = new InProcessTelemetryProbe(tempDiagnosticsDir);
            var shutdown = new FakeShutdownPort();
            var notifications = new FakeNotificationPort();
            using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
            using var tray = new MockTrayIcon();
            using var popover = new MockPopover();

            using var lifecycle = new HostLifecycle(
                port,
                shutdown,
                notifications,
                tray,
                popover,
                null,
                null,
                probe);

            lifecycle.ShutdownCoordinator.DryRun = false;

            UpsTelemetry? healthy = await lifecycle.StepAsync();
            Assert.NotNull(healthy);
            Assert.Same(healthy, lifecycle.CurrentTelemetry);
            Assert.False(Directory.Exists(tempDiagnosticsDir));

            // Inject implausible 4.27 Hz + critical low battery state -> must be quarantined before ShutdownCoordinator/UI
            port.NextTelemetry = OutageLowTelemetry with { FrequencyHz = 4.27 };
            UpsTelemetry? quarantinedResult1 = await lifecycle.StepAsync();
            UpsTelemetry? quarantinedResult2 = await lifecycle.StepAsync();

            Assert.Same(healthy, quarantinedResult1);
            Assert.Same(healthy, quarantinedResult2);
            Assert.Same(healthy, lifecycle.CurrentTelemetry);
            Assert.Equal(0, shutdown.Calls);
        }
        finally
        {
            if (Directory.Exists(tempDiagnosticsDir))
            {
                Directory.Delete(tempDiagnosticsDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenContextMenuFromTray_DispatchesOpenPauseResumeTestBeeperAndExitActions()
    {
        using var port = new MockUpsPort { NextTelemetry = LineNormalTelemetry };
        using var tray = new MockTrayIcon();
        using var popover = new MockPopover();
        var exitDialog = new MockExitDialog { Result = ExitDialogResult.Cancel };
        var menu = new Win32ContextMenu(enableNativeMenu: false);

        using var lifecycle = new HostLifecycle(
            port,
            new FakeShutdownPort(),
            new FakeNotificationPort(),
            tray,
            popover,
            exitDialog,
            contextMenu: menu);

        await lifecycle.StepAsync();

        menu.MockMenuHandler = _ => ContextMenuAction.Open;
        lifecycle.OpenContextMenuFromTray();
        Assert.True(popover.IsVisible);

        menu.MockMenuHandler = _ => ContextMenuAction.ToggleUsbMonitoring;
        lifecycle.OpenContextMenuFromTray();
        Assert.True(lifecycle.IsPaused);
        lifecycle.OpenContextMenuFromTray();
        Assert.False(lifecycle.IsPaused);

        menu.MockMenuHandler = _ => ContextMenuAction.BatteryTest;
        lifecycle.OpenContextMenuFromTray();

        menu.MockMenuHandler = _ => ContextMenuAction.ToggleBeeper;
        lifecycle.OpenContextMenuFromTray();

        bool exitRequested = false;
        lifecycle.ExitAction = () => exitRequested = true;
        exitDialog.Result = ExitDialogResult.Close;
        menu.MockMenuHandler = _ => ContextMenuAction.Exit;
        lifecycle.OpenContextMenuFromTray();
        Assert.True(exitRequested);
    }
}
