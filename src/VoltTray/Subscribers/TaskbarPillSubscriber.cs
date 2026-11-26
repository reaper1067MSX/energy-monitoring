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
    private readonly HostLifecycle _lifecycle;

    public TaskbarPillSubscriber(IEventBus eventBus, ITaskbarPillView pillView, HostLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _pillView = pillView ?? throw new ArgumentNullException(nameof(pillView));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

        _pillView.OnPillLeftClicked += HandleLeftClick;
        _pillView.OnPillRightClicked += HandleRightClick;
        _telemetrySubscription = eventBus.Subscribe<TelemetryReceived>(received => _pillView.Update(received.Telemetry));
    }

    public void Dispose()
    {
        _pillView.OnPillLeftClicked -= HandleLeftClick;
        _pillView.OnPillRightClicked -= HandleRightClick;
        _telemetrySubscription.Dispose();
    }

    private void HandleLeftClick() => _lifecycle.TogglePopoverFromTray();
    private void HandleRightClick() => _lifecycle.OpenContextMenuFromTray();
}
