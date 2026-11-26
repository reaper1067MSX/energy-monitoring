using VoltTray.Core.Events;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.Hosting;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.Shared;
using VoltTray.Subscribers;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Models;
using VoltTray.UI.Presenters;
using VoltTray.UI.Rendering;
using VoltTray.UI.Views;

namespace VoltTray;

public sealed class HostLifecycle : IDisposable
{
    private readonly IUpsDevicePort _upsPort;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly ITrayIconView _trayRenderer;
    private readonly IPopoverView _popoverPainter;
    private readonly IExitDialogView _exitDialog;
    private readonly IScreenPlacementProvider _screenPlacementProvider;
    private readonly InProcessTelemetryProbe _telemetryProbe;
    private readonly IExecutionBoundary _executionBoundary;
    private readonly bool _delegateDiagnosticsToPort;
    private readonly SplitCockpitPresenter _presenter;
    private readonly IEventBus _eventBus;
    private readonly List<IDisposable> _subscriptions = new();
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private Timer? _pauseTimer;
    private DateTimeOffset? _pausedUntil;
    private ViewRect? _lastAnchorRect;
    private int _consecutiveErrors;

    public UpsTelemetry? CurrentTelemetry => _presenter.CurrentTelemetry;
    public UpsOperationalState OperationalState { get; private set; } = UpsOperationalState.CommunicationsLost;
    public bool IsPaused => _pausedUntil.HasValue;
    public DateTimeOffset? PausedUntil => _pausedUntil;
    public Action? ExitAction { get; set; }
    public ITrayIconView TrayRenderer => _trayRenderer;
    public IPopoverView PopoverPainter => _popoverPainter;
    public ShutdownCoordinator ShutdownCoordinator => _shutdownCoordinator;
    public IUpsDevicePort UpsPort => _upsPort;

    public HostLifecycle(
        IUpsDevicePort upsPort,
        ISafetyShutdownPort? shutdownPort = null,
        INotificationPort? notificationPort = null,
        ITrayIconView? trayRenderer = null,
        IPopoverView? popoverPainter = null,
        IExitDialogView? exitDialog = null,
        IScreenPlacementProvider? screenPlacementProvider = null,
        InProcessTelemetryProbe? telemetryProbe = null,
        IExecutionBoundary? executionBoundary = null,
        bool delegateDiagnosticsToPort = false,
        IEventBus? eventBus = null)
    {
        _upsPort = upsPort ?? throw new ArgumentNullException(nameof(upsPort));
        _trayRenderer = trayRenderer ?? new TrayRenderer();
        _popoverPainter = popoverPainter ?? new PopoverPainter();
        _exitDialog = exitDialog ?? new ExitDialog();
        _screenPlacementProvider = screenPlacementProvider ?? new Win32ScreenPlacementProvider();
        _telemetryProbe = telemetryProbe ?? new InProcessTelemetryProbe();
        _executionBoundary = executionBoundary ?? new SafeExecutionMiddleware(_telemetryProbe);
        _delegateDiagnosticsToPort = delegateDiagnosticsToPort;
        _shutdownCoordinator = new ShutdownCoordinator(
            shutdownPort ?? new WindowsShutdownPort(),
            notificationPort ?? new WindowsNotificationPort());
        _presenter = new SplitCockpitPresenter(
            _trayRenderer,
            _popoverPainter,
            () => _upsPort.ToggleBeeperAsync(),
            () => _upsPort.TriggerSelfTestAsync());

        _eventBus = eventBus ?? new InProcessEventBus(_executionBoundary);
        _subscriptions.Add(new ShutdownSubscriber(_eventBus, _shutdownCoordinator));
        _subscriptions.Add(new TrayStatusSubscriber(_eventBus, _trayRenderer, _presenter));
        _subscriptions.Add(_eventBus.Subscribe<TelemetryReceived>(RefreshVisiblePopover));

        _popoverPainter.OnSelfTestRequested += HandleSelfTestRequested;
        _popoverPainter.OnToggleBeeperRequested += HandleToggleBeeperRequested;
        _trayRenderer.OnTrayIconClicked += HandleTrayIconClicked;
        _trayRenderer.OnContextMenuRequested += HandleContextMenuRequested;
    }

    public HostLifecycle(IUpsDevicePort port, ITrayIconView tray, IPopoverView popover)
        : this(port, null, null, tray, popover)
    {
    }

    public void Start(int intervalMs = ApplicationDefaults.DefaultPollingIntervalMs)
    {
        if (_pollingTask != null)
        {
            return;
        }

        _eventBus.Publish(new MonitoringStatusChanged(OperationalState));

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _pollingTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            int reconnectBackoffMs = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Result<UpsTelemetry?> loopResult = await _executionBoundary
                        .ExecuteAsync(ct => PollOnceAsync(ct), "HostLifecycle.PollLoop", token)
                        .ConfigureAwait(false);

                    bool isHealthySample = loopResult.IsSuccess && loopResult.Value != null && _consecutiveErrors == 0;
                    if (isHealthySample)
                    {
                        reconnectBackoffMs = 0;
                        await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
                        continue;
                    }

                    reconnectBackoffMs = ComputeNextReconnectBackoffMs(intervalMs, reconnectBackoffMs);
                    await Task.Delay(reconnectBackoffMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public async Task<UpsTelemetry?> PollOnceAsync(CancellationToken token = default, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        if (_pausedUntil.HasValue)
        {
            if (timestamp >= _pausedUntil.Value)
            {
                Resume();
            }
            else
            {
                OperationalState = UpsOperationalState.Paused;
                _eventBus.Publish(new MonitoringStatusChanged(UpsOperationalState.Paused));
                return CurrentTelemetry;
            }
        }

        Result<UpsTelemetry?> pollResult = await _executionBoundary
            .ExecuteAsync(ct => _upsPort.ReadTelemetryAsync(timestamp, ct), "HostLifecycle.ReadTelemetry", token)
            .ConfigureAwait(false);
        UpsTelemetry? telemetry = pollResult.IsSuccess ? pollResult.Value : null;

        if (telemetry == null)
        {
            _consecutiveErrors++;
            if (_consecutiveErrors >= HostConstants.ConsecutiveErrorsBeforeCommLost || CurrentTelemetry == null)
            {
                OperationalState = UpsOperationalState.CommunicationsLost;
                _eventBus.Publish(new MonitoringStatusChanged(UpsOperationalState.CommunicationsLost));
            }

            return CurrentTelemetry;
        }

        if (!_delegateDiagnosticsToPort)
        {
            TelemetryAnomalyKind? physicsAnomaly = TelemetryPlausibilityGuard.EvaluatePhysics(telemetry, CurrentTelemetry);
            if (physicsAnomaly != null)
            {
                _telemetryProbe.Record(
                    ReadOnlySpan<byte>.Empty,
                    telemetry,
                    physicsAnomaly,
                    "Quarantined in HostLifecycle.PollOnceAsync");
                _telemetryProbe.DumpRecent();
                return CurrentTelemetry;
            }

            _telemetryProbe.Record(ReadOnlySpan<byte>.Empty, telemetry);
        }
        _consecutiveErrors = 0;
        OperationalState = telemetry.State;
        _eventBus.Publish(new TelemetryReceived(telemetry, timestamp));

        return telemetry;
    }

    private void RefreshVisiblePopover(TelemetryReceived received)
    {
        if (!_popoverPainter.IsVisible)
        {
            return;
        }

        (ViewRect anchorRect, ViewRect workArea, TaskbarEdge edge) = ResolvePlacement();
        _popoverPainter.Show(received.Telemetry, _popoverPainter.CurrentTheme, anchorRect, edge, workArea);
    }

    public Task<UpsTelemetry?> StepAsync(CancellationToken token = default, DateTimeOffset? now = null)
    {
        return PollOnceAsync(token, now);
    }

    public void Pause(TimeSpan? duration = null, DateTimeOffset? now = null)
    {
        TimeSpan span = duration ?? TimeSpan.FromMinutes(ApplicationDefaults.DefaultPauseMinutes);
        _pausedUntil = (now ?? DateTimeOffset.UtcNow) + span;
        OperationalState = UpsOperationalState.Paused;
        _upsPort.Disconnect();
        _eventBus.Publish(new MonitoringStatusChanged(UpsOperationalState.Paused));
        _pauseTimer?.Dispose();
        int timerDueMs = (int)Math.Min(Math.Max(HostConstants.MinimumPauseTimerMs, span.TotalMilliseconds), int.MaxValue);
        _pauseTimer = new Timer(_ => Resume(), null, timerDueMs, Timeout.Infinite);
    }

    public void PauseMonitoring(TimeSpan? duration = null)
    {
        Pause(duration);
    }

    public void Resume()
    {
        _pauseTimer?.Dispose();
        _pauseTimer = null;
        _pausedUntil = null;
        _upsPort.TryConnect();
    }

    public void ResumeMonitoring()
    {
        Resume();
    }

    public ExitDialogResult ShowExitDialog(UITheme theme = UITheme.Dark, nint ownerHandle = 0)
    {
        ExitDialogResult result = _exitDialog.Show(theme, ownerHandle);
        if (result == ExitDialogResult.Pause)
        {
            Pause();
        }
        else if (result == ExitDialogResult.Close)
        {
            RequestExit();
        }

        return result;
    }

    public void RequestExit()
    {
        Stop();
        ExitAction?.Invoke();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _pollingTask?.Wait(HostConstants.PollingStopTimeoutMs);
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException aggregateException) when (
            aggregateException.InnerExceptions.All(ex => ex is OperationCanceledException))
        {
        }

        _pollingTask = null;
        _cts?.Dispose();
        _cts = null;
        _pauseTimer?.Dispose();
        _pauseTimer = null;
        _upsPort.Disconnect();
    }

    public void Dispose()
    {
        _popoverPainter.OnSelfTestRequested -= HandleSelfTestRequested;
        _popoverPainter.OnToggleBeeperRequested -= HandleToggleBeeperRequested;
        _trayRenderer.OnTrayIconClicked -= HandleTrayIconClicked;
        _trayRenderer.OnContextMenuRequested -= HandleContextMenuRequested;

        Stop();
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _trayRenderer.Dispose();
        _popoverPainter.Dispose();
        _upsPort.Dispose();
    }

    private void HandleSelfTestRequested()
    {
        RunCommandInBackground(_presenter.TriggerSelfTestAsync, HostConstants.TriggerSelfTestOperation);
    }

    private void HandleToggleBeeperRequested()
    {
        RunCommandInBackground(_presenter.ToggleBeeperAsync, HostConstants.ToggleBeeperOperation);
    }

    private void RunCommandInBackground(Func<Task<bool>> command, string operationName)
    {
        _ = _executionBoundary.ExecuteAsync(_ => command(), operationName);
    }

    private void HandleContextMenuRequested()
    {
        ShowExitDialog();
    }

    private void HandleTrayIconClicked()
    {
        (ViewRect anchorRect, ViewRect workArea, TaskbarEdge edge) = ResolvePlacement();
        _lastAnchorRect = anchorRect;

        if (CurrentTelemetry != null)
        {
            _presenter.TogglePopover(anchorRect, edge, workArea, _popoverPainter.CurrentTheme);
            return;
        }

        if (_popoverPainter.IsVisible)
        {
            _popoverPainter.Hide();
            return;
        }

        _popoverPainter.Show(UpsTelemetry.CreateDefault(), _popoverPainter.CurrentTheme, anchorRect, edge, workArea);
    }

    private (ViewRect AnchorRect, ViewRect WorkArea, TaskbarEdge Edge) ResolvePlacement()
    {
        ViewRect? liveIconRect = _trayRenderer.TryGetIconRect() ?? _lastAnchorRect;
        if (liveIconRect is ViewRect resolvedIconRect)
        {
            (ViewRect workArea, TaskbarEdge edge) = _screenPlacementProvider.GetWorkAreaForAnchor(resolvedIconRect);
            return (resolvedIconRect, workArea, edge);
        }

        (ViewRect fallbackWorkArea, TaskbarEdge fallbackEdge) = _screenPlacementProvider.GetWorkAreaForAnchor(default);
        int anchorLeft = fallbackEdge == TaskbarEdge.Left
            ? fallbackWorkArea.Left
            : Math.Max(fallbackWorkArea.Left, fallbackWorkArea.Right - HostConstants.FallbackAnchorOffsetPx);
        int anchorTop = fallbackEdge == TaskbarEdge.Top
            ? fallbackWorkArea.Top
            : Math.Max(fallbackWorkArea.Top, fallbackWorkArea.Bottom - HostConstants.FallbackAnchorSizePx);
        var fallbackAnchor = new ViewRect(
            anchorLeft,
            anchorTop,
            anchorLeft + HostConstants.FallbackAnchorSizePx,
            anchorTop + HostConstants.FallbackAnchorSizePx);

        return (fallbackAnchor, fallbackWorkArea, fallbackEdge);
    }

    private static int ComputeNextReconnectBackoffMs(int baseIntervalMs, int currentBackoffMs)
    {
        int initialDelayMs = Math.Max(HostConstants.MinimumPauseTimerMs, baseIntervalMs);
        if (currentBackoffMs <= 0)
        {
            return initialDelayMs;
        }

        int maxBackoffMs = Math.Max(initialDelayMs, HostConstants.MaxReconnectBackoffMs);
        return Math.Min(currentBackoffMs * HostConstants.BackoffMultiplier, maxBackoffMs);
    }
}
