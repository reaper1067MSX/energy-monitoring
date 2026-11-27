using VoltTray.Core.Events;
using VoltTray.UI.TaskbarPill;

namespace VoltTray.Subscribers;

/// <summary>
/// Keeps the opt-in taskbar pill synced with accepted telemetry and routes
/// left/right pill clicks back into HostLifecycle.
/// </summary>
public sealed class TaskbarPillSubscriber : IDisposable
{
    private readonly IDisposable _telemetrySubscription;
    private readonly ITaskbarPillView _pillView;
    private readonly IHostLifecycle _lifecycle;
    private readonly Func<bool> _isLargeModeProvider;

    public TaskbarPillSubscriber(
        IEventBus eventBus,
        ITaskbarPillView pillView,
        IHostLifecycle lifecycle,
        Func<bool>? isLargeModeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _pillView = pillView ?? throw new ArgumentNullException(nameof(pillView));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _isLargeModeProvider = isLargeModeProvider ?? (() => true);

        _pillView.OnPillLeftClicked += HandleLeftClick;
        _pillView.OnPillRightClicked += HandleRightClick;
        _telemetrySubscription = eventBus.Subscribe<TelemetryReceived>(received =>
        {
            if (_isLargeModeProvider())
            {
                _pillView.Update(received.Telemetry);
            }
        });
    }

    public void Dispose()
    {
        _pillView.OnPillLeftClicked -= HandleLeftClick;
        _pillView.OnPillRightClicked -= HandleRightClick;
        _telemetrySubscription.Dispose();
    }

    private void HandleLeftClick() => _lifecycle.TogglePopoverFromTray(_pillView.CurrentBounds);
    private void HandleRightClick() => _lifecycle.OpenContextMenuFromTray();
}
