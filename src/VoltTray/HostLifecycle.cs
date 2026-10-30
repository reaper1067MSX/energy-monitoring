using System.Text;
using VoltTray.Core.Models;
using VoltTray.Core.Ports;
using VoltTray.Core.Presenters;
using VoltTray.Core.Services;
using VoltTray.Core.Views;
using VoltTray.Infrastructure.OS;
using VoltTray.UI.Dialogs;
using VoltTray.UI.Rendering;

namespace VoltTray;

public sealed class HostLifecycle : IVoltTrayView, IDisposable
{
    public const int DefaultPollingIntervalMs = 1500, DefaultPauseMinutes = 15;
    private readonly IUpsDevicePort _upsPort;
    private readonly VoltronicParser _parser;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly ITrayIconView _trayRenderer;
    private readonly IPopoverView _popoverPainter;
    private readonly IExitDialogView _exitDialog;
    private readonly SplitCockpitPresenter _presenter;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private Timer? _pauseTimer;
    private DateTimeOffset? _pausedUntil;
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

    public HostLifecycle(IUpsDevicePort upsPort, ISafetyShutdownPort? shutdownPort = null, INotificationPort? notificationPort = null,
        ITrayIconView? trayRenderer = null, IPopoverView? popoverPainter = null, IExitDialogView? exitDialog = null, VoltronicParser? parser = null)
    {
        _upsPort = upsPort ?? throw new ArgumentNullException(nameof(upsPort));
        _trayRenderer = trayRenderer ?? new TrayRenderer();
        _popoverPainter = popoverPainter ?? new PopoverPainter();
        _exitDialog = exitDialog ?? new ExitDialog();
        _parser = parser ?? new VoltronicParser();
        _shutdownCoordinator = new ShutdownCoordinator(shutdownPort ?? new WindowsShutdownPort(), notificationPort ?? new WindowsNotificationPort());
        _presenter = new SplitCockpitPresenter(this, _trayRenderer, _popoverPainter, cmd => _upsPort.SendCommandAsync(cmd).GetAwaiter().GetResult());
        if (_popoverPainter is PopoverPainter p) { p.OnSelfTestRequested += () => _presenter.TriggerSelfTest(); p.OnToggleBeeperRequested += () => _presenter.ToggleBeeper(); }
    }

    public HostLifecycle(IUpsDevicePort port, ITrayIconView tray, IPopoverView popover) : this(port, null, null, tray, popover) { }

    public void Start(int intervalMs = DefaultPollingIntervalMs)
    {
        if (_pollingTask != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pollingTask = Task.Run(async () => {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            while (!token.IsCancellationRequested) {
                try { await PollOnceAsync(token); await timer.WaitForNextTickAsync(token); }
                catch (OperationCanceledException) { break; } catch { }
            }
        }, token);
    }

    public async Task<UpsTelemetry?> PollOnceAsync(CancellationToken token = default, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        if (_pausedUntil.HasValue) {
            if (timestamp >= _pausedUntil.Value) Resume();
            else {
                OperationalState = UpsOperationalState.Paused;
                _trayRenderer.UpdateIcon(UpsOperationalState.Paused, 0, 96);
                _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(UpsOperationalState.Paused, 0, 0));
                return CurrentTelemetry;
            }
        }
        string? response = null;
        try { response = await _upsPort.QueryAsync("QS\r", token); } catch { response = null; }
        if (string.IsNullOrEmpty(response)) {
            if (++_consecutiveErrors >= 3 || CurrentTelemetry == null) {
                OperationalState = UpsOperationalState.CommunicationsLost;
                _trayRenderer.UpdateIcon(UpsOperationalState.CommunicationsLost, 0, 96);
                _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(UpsOperationalState.CommunicationsLost, 0, 0));
            }
            return CurrentTelemetry;
        }
        _consecutiveErrors = 0;
        var telemetry = _parser.Parse(Encoding.Latin1.GetBytes(response), CurrentTelemetry, timestamp);
        if (telemetry != null) {
            OperationalState = telemetry.State;
            _shutdownCoordinator.Evaluate(telemetry, timestamp);
            _presenter.UpdateTelemetry(telemetry);
            if (_popoverPainter.IsVisible) {
                var theme = (_popoverPainter as PopoverPainter)?.CurrentTheme ?? UITheme.Dark;
                var iconRect = (_popoverPainter as PopoverPainter)?.CurrentIconRect ?? new ViewRect(1800, 1040, 1832, 1072);
                _popoverPainter.Show(telemetry, theme, iconRect, TaskbarEdge.Bottom, new ViewRect(0, 0, 1920, 1080));
            }
        }
        return telemetry;
    }

    public Task<UpsTelemetry?> StepAsync(CancellationToken token = default, DateTimeOffset? now = null) => PollOnceAsync(token, now);
    public void Pause(TimeSpan? duration = null, DateTimeOffset? now = null)
    {
        TimeSpan span = duration ?? TimeSpan.FromMinutes(DefaultPauseMinutes);
        _pausedUntil = (now ?? DateTimeOffset.UtcNow) + span;
        OperationalState = UpsOperationalState.Paused;
        _upsPort.Disconnect();
        _trayRenderer.UpdateIcon(UpsOperationalState.Paused, 0, 96);
        _trayRenderer.SetTooltip(SplitCockpitPresenter.FormatTooltip(UpsOperationalState.Paused, 0, 0));
        _pauseTimer?.Dispose();
        _pauseTimer = new Timer(_ => Resume(), null, (int)Math.Min(Math.Max(1, span.TotalMilliseconds), int.MaxValue), Timeout.Infinite);
    }
    public void PauseMonitoring(TimeSpan? duration = null) => Pause(duration);
    public void Resume() { _pauseTimer?.Dispose(); _pauseTimer = null; _pausedUntil = null; _upsPort.TryConnect(); }
    public void ResumeMonitoring() => Resume();
    public ExitDialogResult ShowExitDialog(UITheme theme = UITheme.Dark, nint ownerHandle = 0)
    {
        var result = _exitDialog.Show(theme, ownerHandle);
        if (result == ExitDialogResult.Pause) Pause(); else if (result == ExitDialogResult.Close) RequestExit();
        return result;
    }
    public void RequestExit() { Stop(); ExitAction?.Invoke(); }
    public void Stop() { _cts?.Cancel(); try { _pollingTask?.Wait(500); } catch { } _pollingTask = null; _cts?.Dispose(); _cts = null; _pauseTimer?.Dispose(); _pauseTimer = null; _upsPort.Disconnect(); }
    public void Initialize() { }
    public void UpdateTelemetry(UpsTelemetry telemetry) { }
    public void SetOperationalState(UpsOperationalState state) => OperationalState = state;
    public void Dispose() { Stop(); _trayRenderer.Dispose(); _popoverPainter.Dispose(); _upsPort.Dispose(); }
}
