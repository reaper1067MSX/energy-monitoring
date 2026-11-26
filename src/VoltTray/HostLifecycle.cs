using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Services;
using VoltTray.Infrastructure.Diagnostics;
using VoltTray.Infrastructure.OS;
using VoltTray.Shared;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Models;
using VoltTray.UI.Presenters;
using VoltTray.UI.Rendering;
using VoltTray.UI.Tokens;
using VoltTray.UI.Views;

namespace VoltTray;

public sealed class HostLifecycle : IDisposable
{
    private const int ConsecutiveErrorsBeforeCommLost = 3;
    private const int PollingStopTimeoutMs = 500;
    private const int MinimumPauseTimerMs = 1;
    private const int FallbackAnchorSizePx = 32;
    private const int FallbackAnchorOffsetPx = 40;

    private readonly IUpsDevicePort _upsPort;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly ITrayIconView _trayRenderer;
    private readonly IPopoverView _popoverPainter;
    private readonly IExitDialogView _exitDialog;
    private readonly IScreenPlacementProvider _screenPlacementProvider;
    private readonly InProcessTelemetryProbe _telemetryProbe;
    private readonly SplitCockpitPresenter _presenter;

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
        InProcessTelemetryProbe? telemetryProbe = null)
    {
        _upsPort = upsPort ?? throw new ArgumentNullException(nameof(upsPort));
        _trayRenderer = trayRenderer ?? new TrayRenderer();
        _popoverPainter = popoverPainter ?? new PopoverPainter();
        _exitDialog = exitDialog ?? new ExitDialog();
        _screenPlacementProvider = screenPlacementProvider ?? new Win32ScreenPlacementProvider();
        _telemetryProbe = telemetryProbe ?? new InProcessTelemetryProbe();
        _shutdownCoordinator = new ShutdownCoordinator(
            shutdownPort ?? new WindowsShutdownPort(),
            notificationPort ?? new WindowsNotificationPort());
        _presenter = new SplitCockpitPresenter(
            _trayRenderer,
            _popoverPainter,
            () => _upsPort.ToggleBeeperAsync().GetAwaiter().GetResult(),
            () => _upsPort.TriggerSelfTestAsync().GetAwaiter().GetResult());

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

        _trayRenderer.UpdateIcon(OperationalState, 0, DesignTokens.Sizes.DefaultDpi);
        _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(OperationalState, 0, null));

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _pollingTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(token);
                    await timer.WaitForNextTickAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
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
                _trayRenderer.UpdateIcon(UpsOperationalState.Paused, 0, DesignTokens.Sizes.DefaultDpi);
                _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(UpsOperationalState.Paused, 0, null));
                return CurrentTelemetry;
            }
        }

        UpsTelemetry? telemetry = null;
        try
        {
            telemetry = await _upsPort.ReadTelemetryAsync(timestamp, token);
        }
        catch
        {
            telemetry = null;
        }

        if (telemetry == null)
        {
            _consecutiveErrors++;
            if (_consecutiveErrors >= ConsecutiveErrorsBeforeCommLost || CurrentTelemetry == null)
            {
                OperationalState = UpsOperationalState.CommunicationsLost;
                _trayRenderer.UpdateIcon(UpsOperationalState.CommunicationsLost, 0, DesignTokens.Sizes.DefaultDpi);
                _trayRenderer.SetTooltip(
                    SplitCockpitPresenter.FormatTooltip(UpsOperationalState.CommunicationsLost, 0, null));
            }

            return CurrentTelemetry;
        }

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
        _consecutiveErrors = 0;
        OperationalState = telemetry.State;
        _shutdownCoordinator.Evaluate(telemetry, timestamp);
        _presenter.UpdateTelemetry(telemetry);

        if (_popoverPainter.IsVisible)
        {
            (ViewRect anchorRect, ViewRect workArea, TaskbarEdge edge) = ResolvePlacement();
            _popoverPainter.Show(telemetry, _popoverPainter.CurrentTheme, anchorRect, edge, workArea);
        }

        return telemetry;
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
        _trayRenderer.UpdateIcon(UpsOperationalState.Paused, 0, DesignTokens.Sizes.DefaultDpi);
        _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(UpsOperationalState.Paused, 0, null));
        _pauseTimer?.Dispose();
        int timerDueMs = (int)Math.Min(Math.Max(MinimumPauseTimerMs, span.TotalMilliseconds), int.MaxValue);
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
            _pollingTask?.Wait(PollingStopTimeoutMs);
        }
        catch
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
        _trayRenderer.Dispose();
        _popoverPainter.Dispose();
        _upsPort.Dispose();
    }

    private void HandleSelfTestRequested()
    {
        _presenter.TriggerSelfTest();
    }

    private void HandleToggleBeeperRequested()
    {
        _presenter.ToggleBeeper();
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
            : Math.Max(fallbackWorkArea.Left, fallbackWorkArea.Right - FallbackAnchorOffsetPx);
        int anchorTop = fallbackEdge == TaskbarEdge.Top
            ? fallbackWorkArea.Top
            : Math.Max(fallbackWorkArea.Top, fallbackWorkArea.Bottom - FallbackAnchorSizePx);
        var fallbackAnchor = new ViewRect(
            anchorLeft,
            anchorTop,
            anchorLeft + FallbackAnchorSizePx,
            anchorTop + FallbackAnchorSizePx);

        return (fallbackAnchor, fallbackWorkArea, fallbackEdge);
    }
}
